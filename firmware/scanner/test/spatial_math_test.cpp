#include "pan_motion.h"
#include "scan_geometry.h"
#include "step_history.h"
#include "scan_trace.h"
#include "control_command_parser.h"
#include "../../common/uwb_twr.h"
#include "../../common/trilateration_math.h"
#include <cassert>
#include <cstdio>
#include <cmath>

static bool near(double a,double b,double eps=1e-5) { return std::fabs(a-b)<eps; }
static void history() {
    StepHistory<4> h; int32_t s;
    assert(!h.at(0,s));
    h.push(100,0); h.push(200,1); h.push(300,2);
    assert(!h.at(99,s)); assert(h.at(199,s) && s==0);
    assert(h.at(200,s) && s==1); assert(h.at(299,s) && s==1);
    h.push(400,1); h.push(500,0);
    assert(!h.at(100,s)); assert(h.at(450,s) && s==1);
    h.clear(); h.push(0xfffffff0,7); h.push(0x10,8);
    assert(h.at(0,s) && s==7); assert(h.at(0x10,s) && s==8);
}
static void geometry() {
    using namespace ScanGeometry;
    Vec3 origin={0,0.147f,0.079f};
    Vec3 p=inHead(2,0,90,origin);
    assert(near(p.x,2) && near(p.y,.147) && near(p.z,.079));
    Vec3 p90=project(p,90,0,0,true,{0,0,0},true);
    assert(near(p90.x,.079) && near(p90.y,.147) && near(p90.z,-2));
    Vec3 orbit=project(origin,180,0,0,true,{0,0,0},true);
    assert(near(orbit.z,-.079));
    Vec3 tagged=project(p,90,0,0,true,origin,true);
    assert(near(tagged.x,0) && near(tagged.y,0) && near(tagged.z,-2));
    // Same body tilt represented in head coordinates must compose in that order.
    Vec3 a=project(p,45,10,20,true,{0,0,0},true);
    Vec3 b=yaw(tilt(p,10,20),45);
    assert(near(a.x,b.x) && near(a.y,b.y) && near(a.z,b.z));
    // Forward ray with mountYaw=0 shoots in +Z
    Vec3 pForward=inHead(2,0,0,origin);
    assert(near(pForward.x,0) && near(pForward.y,.147) && near(pForward.z,2.079));
    Vec3 pForward90=project(pForward,90,0,0,false,{0,0,0},false);
    assert(near(pForward90.x,2.079) && near(pForward90.y,.147) && near(pForward90.z,0));
    // Synthetic forward wall z=2: every pan/ray intersection returns to that plane.
    for (int pan=-60;pan<=60;pan+=5) {
        Vec3 o=yaw(origin,pan), ray=yaw({0,0,1},pan);
        float distance=(2-o.z)/ray.z;
        Vec3 hit=project(inHead(distance,0,0,origin),pan,0,0,false,{0,0,0},false);
        assert(near(hit.z,2));
    }
}
// Measured mounting: forward eccentricity; scan plane transverse to that offset.
static void verticalMount() {
    using namespace ScanGeometry;
    const Vec3 offset = {0,.050f,.090f};
    const Vec3 expected[] = {{0,1.050f,.090f},{-1,.050f,.090f},
                             {0,-.950f,.090f},{1,.050f,.090f}};
    for (int i=0;i<4;++i) {
        auto debug = inHeadDebug(1,i*90,1,90,90,offset);
        assert(near(debug.angleDeg,90+i*90) && near(debug.mountYawDeg,90));
        assert(near(debug.point.x,expected[i].x));
        assert(near(debug.point.y,expected[i].y));
        assert(near(debug.point.z,expected[i].z));
    }
    auto reverse = inHeadDebug(1,90,-1,90,90,offset);
    assert(near(reverse.point.x,1));
    // Clockwise +90 moves the forward optical offset to +X; CCW mirrors it.
    auto up = inHeadDebug(1,0,1,90,90,offset).point;
    auto cw = project(up,90,0,0,true,{0,0,0},true);
    auto ccw = project(up,-90,0,0,true,{0,0,0},true);
    assert(near(cw.x,.09) && near(ccw.x,-.09));
    assert(near(cw.y,1.05) && near(cw.z,0));
    // A complete slice stays in the displaced vertical plane at pan zero.
    for (int raw=0;raw<360;++raw)
        assert(near(inHeadDebug(2,raw,1,90,90,offset).point.z,.09));
    float horizontal, vertical;
    auto forward90 = inHeadDebug(2,90,1,90,90,offset).point;
    assert(thermalAngles(forward90,offset,{0,-.025f,.050f},true,horizontal,vertical));
    assert(near(horizontal,-1.4321,.01) && near(vertical,.7161,.01));
    auto behind = inHeadDebug(2,270,1,90,90,offset).point;
    assert(!thermalAngles(behind,offset,{0,-.025f,.050f},true,horizontal,vertical));
    // Right rotation puts image-right at native top and image-up at native
    // left. Both angular FOVs swap to 75 horizontal x 110 vertical.
    float col, row;
    assert(thermalRawPixelForRightCorrection90(0,0,110,75,col,row));
    assert(near(col,15.5) && near(row,11.5));
    assert(thermalRawPixelForRightCorrection90(37.5,55,110,75,col,row));
    assert(near(col,0) && near(row,0));
    assert(thermalRawPixelForRightCorrection90(-37.5,55,110,75,col,row));
    assert(near(col,0) && near(row,23));
    assert(thermalRawPixelForRightCorrection90(-37.5,-55,110,75,col,row));
    assert(near(col,31) && near(row,23));
    assert(thermalRawPixelForRightCorrection90(37.5,-55,110,75,col,row));
    assert(near(col,31) && near(row,0));
    assert(!thermalRawPixelForRightCorrection90(37.6,0,110,75,col,row));
    assert(!thermalRawPixelForRightCorrection90(0,55.1,110,75,col,row));
    // A 180-degree optical-side correction changes which LiDAR half-plane
    // receives temperatures; it must not rotate or mirror the preview itself.
    assert(thermalAngles({-2,0,1},{0,0,0},{0,0,0},false,horizontal,vertical,0));
    assert(near(horizontal,26.56505,.001));
    assert(!thermalAngles({-2,0,1},{0,0,0},{0,0,0},false,horizontal,vertical,1));
    assert(thermalAngles({2,0,-1},{0,0,0},{0,0,0},false,horizontal,vertical,1));
    assert(near(horizontal,26.56505,.001));
    assert(!thermalAngles({2,0,-1},{0,0,0},{0,0,0},false,horizontal,vertical,0));
    // A horizontal mirror changes both the raw pixel used for a hot LiDAR
    // ray and its preview X. In either profile, +37.5 degrees displays right.
    assert(thermalRawPixelForRightCorrection90(37.5,0,110,75,col,row,0));
    assert(near(row,0) && near(23-row,23));
    assert(thermalRawPixelForRightCorrection90(37.5,0,110,75,col,row,2));
    assert(near(row,23) && near(row,23));
    assert(!thermalRawPixelForRightCorrection90(0,0,110,75,col,row,4));
}
static void ranging() {
    // Simulate two independent oscillators with +20/-15 ppm drift and asymmetric delays.
    double tick=63897600000.0, tof=3.0/299702547.0;
    double ka=1.000020, kb=.999985, db=.006, da=.010;
    float meters;
    assert(uwbDistance(llround((db+2*tof)*tick*ka),llround(db*tick*kb),
        llround((da+2*tof)*tick*kb),llround(da*tick*ka),meters));
    assert(near(meters,3,.01));
    assert(!uwbDistance(1,2,3,4,meters));
    assert(!uwbDistance(0,1,1,1,meters));
}
static void trilateration() {
    using namespace UwbMath;
    Vec a[]={{.077,.035801,0},{0,0,0},{.154,0,0}};
    Vec actual={.077,.02,2}, pos; double r[3],gdop;
    for(int k=0;k<3;++k) r[k]=norm(sub(actual,a[k]));
    assert(solve(a,r,1,pos,gdop));
    assert(near(pos.x,0) && near(pos.y,.02) && near(pos.z,2));
    printf("PCB at 2m: GDOP %.2f; sigma(10cm) %.2fm; sigma(1cm) %.2fm\n",gdop,gdop*.1,gdop*.01);
    assert(gdop*.1>5); // Quantifies why a small PCB cannot anchor a room accurately.
    assert(solve(a,r,-1,pos,gdop) && near(pos.z,-2));
    Vec shifted[3]; for(int k=0;k<3;++k) shifted[k]=add(a[k],{1,2,3});
    assert(solve(shifted,r,1,pos,gdop) && near(pos.z,2));
    r[0]=30; assert(!solve(a,r,1,pos,gdop));
    r[0]=NAN; assert(!solve(a,r,1,pos,gdop));
    a[0]=a[1]; assert(!solve(a,r,1,pos,gdop));
}
static void diagnostics() {
    ScanTrace<3> trace; ScanTraceSample data[3];
    assert(trace.copy(data)==0);
    for (int i=0;i<5;++i) trace.push(100+i,10+i,1000+i,30+i,100+i,2,-3,{.1f,1.05f,.09f},i%2 == 0,500+i,i==4);
    assert(trace.copy(data)==3);
    assert(data[0].sequence==3 && data[2].sequence==5);
    assert(data[0].lidarAngleDeg==12 && data[2].panInputDeg==34);
    assert(data[0].queued && data[2].queued && !data[1].queued);
    assert(data[2].signalStrength == 504 && data[2].strengthWarning && !data[0].strengthWarning);
    assert(near(data[2].projectedM.y,1.05) && near(data[2].pitchDeg,2));
    char csv[384];
    int length = formatScanTrace(csv,sizeof(csv),data[2]);
    assert(length > 0 && length < (int)sizeof(csv));
    assert(strstr(csv,",104.000,2.000,-3.000,100.000,1050.000,90.000,1,504,1\n"));
    assert(strstr(scanTraceCsvHeader,"x_mm,y_mm,z_mm,queued"));
    assert(strstr(scanTraceCsvHeader,"signal_strength,strength_warning"));
    ControlCommandParser parser; ControlCommand c;
    assert(!parser.feed(7,c)); // fragmented hold-pan command
    assert(parser.feed(0,c) && c.type==7 && c.payload[0]==0);
    assert(parser.feed(8,c) && c.type==8); // explicit park has no payload
    assert(parser.feed(3,c) && c.type==3); // heartbeat stays aligned
    assert(!parser.feed(7,c));
    assert(parser.feed(1,c) && c.type==7 && c.payload[0]==1);
}
static void parkingMotion() {
    const int steps = 200;
    for (int mode=0; mode<2; ++mode) for (int start=1; start<steps; ++start) {
        int step=start, direction=panDirectionToZero(step,steps);
        bool running=true, parking=true;
        int pulses=0;
        while (running && pulses<=steps) {
            advancePan(step,direction,running,parking,1,mode,steps);
            ++pulses;
        }
        assert(step==0 && !parking && !running);
        assert(pulses==(start<=steps/2 ? start : steps-start));
        advancePan(step,direction,running,parking,1,mode,steps);
        assert(step==0); // No extra pulse after arrival.
        running=true;
        advancePan(step,direction,running,parking,1,mode,steps);
        assert(step==1); // Resuming leaves zero in the original scan direction.
    }
    int step=0, direction=1; bool running=true, parking=false;
    for (int i=0; i<steps*2; ++i) {
        advancePan(step,direction,running,parking,1,1,steps);
        assert(step>=0 && step<=steps/2);
    }
    running=false;
    int paused=step;
    advancePan(step,direction,running,parking,1,1,steps);
    assert(step==paused);
}
int main() { parkingMotion(); history(); geometry(); verticalMount(); ranging(); trilateration(); diagnostics(); puts("Spatial regression tests passed"); }

#pragma once
#include <math.h>
namespace UwbMath {
struct Vec { double x,y,z; };
inline Vec sub(Vec a, Vec b) { return {a.x-b.x,a.y-b.y,a.z-b.z}; }
inline Vec add(Vec a, Vec b) { return {a.x+b.x,a.y+b.y,a.z+b.z}; }
inline Vec mul(Vec a,double s) { return {a.x*s,a.y*s,a.z*s}; }
inline double dot(Vec a,Vec b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
inline Vec cross(Vec a,Vec b) { return {a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x}; }
inline double norm(Vec a) { return sqrt(dot(a,a)); }
// Inputs follow anchor order; output origin is midpoint of anchors 2 and 3.
inline bool solve(const Vec a[3], const double r[3], double side, Vec &out, double &gdop) {
    for (int i=0;i<3;++i) if (!isfinite(r[i]) || r[i]<0.08 || r[i]>35) return false;
    Vec baseline=sub(a[2],a[1]); double d=norm(baseline);
    if (d<1e-4) return false;
    Vec ex=mul(baseline,1/d), top=sub(a[0],a[1]);
    double i=dot(ex,top); Vec transverse=sub(top,mul(ex,i)); double j=norm(transverse);
    if (j<1e-4) return false;
    Vec ey=mul(transverse,1/j), ez=cross(ex,ey);
    double x=(r[1]*r[1]-r[2]*r[2]+d*d)/(2*d);
    double y=(r[1]*r[1]-r[0]*r[0]+i*i+j*j-2*i*x)/(2*j);
    double z2=r[1]*r[1]-x*x-y*y;
    if (!isfinite(z2) || z2 < -1e-6) return false;
    Vec p=add(a[1],add(mul(ex,x),add(mul(ey,y),mul(ez,(side<0?-1:1)*sqrt(fmax(0,z2))))));
    // Linearized range-noise amplification: Frobenius norm of inverse Jacobian.
    Vec u[3];
    for (int k=0;k<3;++k) u[k]=mul(sub(p,a[k]),1/r[k]);
    Vec c0=cross(u[1],u[2]), c1=cross(u[2],u[0]), c2=cross(u[0],u[1]);
    double det=dot(u[0],c0);
    if (fabs(det)<1e-9) return false;
    gdop=sqrt(dot(c0,c0)+dot(c1,c1)+dot(c2,c2))/fabs(det);
    out=sub(p,mul(add(a[1],a[2]),0.5));
    return isfinite(gdop);
}
}

# Revisão técnica do AR Scanner

Revisão iniciada em 20/09/2026. Escopo: comparar a proposta de TCC com a implementação, corrigir defeitos verificáveis e identificar decisões que dependem de montagem e uso. A comunicação por rede foi confirmada pelo autor; a alternativa USB permanece uma possibilidade futura.

Este documento distingue presença de código, validação por software e validação física. A leitura do repositório e a compilação não demonstram precisão de sensores, estabilidade em voo ou funcionamento conjunto das três âncoras.

## Proposta e fluxo atual

O scanner ESP32-S3 combina medições LiDAR, ângulo do motor, inclinação e amostras térmicas. A nuvem é enviada por TCP `8888`; prévias de câmera usam HTTP `8889`. A base ESP32-WROOM-32 calcula posição a partir de três distâncias UWB e transmite registros por UDP `9999`. Unity usa a posição relativa, sua referência ARCore e as coordenadas dos pontos para apresentar a nuvem.

Há dois registros binários distintos, ambos de 28 bytes: `ScanPointPacket` e `UwbPositionPacket`. O primeiro usa milímetros para pontos; o segundo usa metros para posição e distâncias. Eles não são intercambiáveis. O fluxo de pontos implementado é uma sequência de registros de tamanho fixo, sem o cabeçalho de lote sugerido por algumas constantes antigas.

## Implementado e pendente

| Área | Evidência no repositório | Limite ou próxima etapa |
| --- | --- | --- |
| Aquisição LiDAR, térmica e IMU | Drivers em `firmware/scanner/src/` | Ensaiar temporização, sinais dos eixos, distância, FOV e alinhamento mecânico |
| Rede de pontos e controle | `main.cpp` do scanner e `PointCloudTcpReceiver.cs` | Testar pausas, desconexão e perda de sessão com placas; sem protocolo versionado |
| Prévia térmica HTTP | `/thermal` e consumidor Unity | Calibrar temperatura, emissividade e relação com o feixe LiDAR |
| Câmera RGB | Driver e `/rgb` existem | Pinagem OV2640 desativada; habilitar somente após confirmar placa e conexão |
| Posição UWB | Gerenciador de rádios, trilateração e `UwbDataReceiver.cs` | Validar TWR, calibração de antenas e multiplexação de três rádios reais |
| Filtro de posição | Suavização Kalman escalar na base | EKF com modelo de movimento e rejeição estatística de multipath não implementado |
| Visualização térmica/RGB | `ThermalPointCloudRenderer.cs`, partículas e HUD | Conferir no Android materiais, transparência, oclusão e preservação de hotspots |
| Redução de nuvem | Voxelização e decimação por flag | Não há detecção planar RANSAC nem reconstrução em quads |
| Simulação | `PointCloudSimulator.cs` e modo no menu | Dados sintéticos; não substitui ensaio integrado |
| Exportação | Exportação PLY no renderizador | Validar coordenadas e unidades contra alvo medido |
| Orientação completa | Pitch/roll no registro de pontos; offset de yaw no Unity | Referência de heading do scanner não resolvida |
| Sincronização | Timestamps locais do scanner e da base | Sem relógio compartilhado, histórico de poses e interpolação por instante de aquisição |
| USB Android | Saída serial no firmware da base | Não existe leitor USB no aplicativo atual |

## Divergências entre os documentos

Os planos históricos foram preservados para registrar a evolução; o README descreve agora o caminho executável atual.

| Assunto | Divergência | Referência operacional |
| --- | --- | --- |
| Geração do projeto | O README anterior descrevia dois celulares com ARCore/UDP; o TCC descreve scanner embarcado | Firmware ESP32 e aplicativo visualizador atuais |
| Transporte | `tcc.md` prevê UDP para nuvem; `implementation_plan.md` prevê TCP e USB para UWB | TCP para pontos, HTTP para imagens e UDP para UWB; rede confirmada pelo autor |
| Núcleos ESP32-S3 | `tcc.md` atribui LiDAR/câmera ao Core 0; plano atribui sensores ao Core 1 e rede ao Core 0 | Conferir criação das tarefas em `firmware/scanner/src/main.cpp` |
| Pinagem UWB scanner | `tcc.md` lista MOSI/MISO 39/40; documento de hardware e firmware usam 4/5 | Confirmar o esquema da placa montada antes de alterar |
| Modelo de placa | Freenove WROOM CAM versus DevKitC N16R8V | Firmware declara `esp32-s3-devkitc-1`; câmera atualmente sem pinagem |
| Regulador 5 V | MP1584EN em `tcc.md`, Mini360 no documento de hardware | Depende da PCB física |
| Algoritmos | Plano afirma EKF e simplificação planar em quads | Código contém filtro escalar e redução de pontos |
| Taxa de lotes | Plano menciona 100/120 lotes/s para 2.000 pontos/s e grupos de 15 | A conta nominal é aproximadamente 133 lotes/s; 15 registros somam 420 bytes; flush parcial pode aumentar a quantidade de escritas |

## Limites espaciais que precisam de decisão

### Posição não determina orientação

Uma tag UWB fornece posição, não a orientação do corpo do scanner. Os registros enviados contêm pitch e roll, mas não yaw. O método `getYaw()` do driver IMU existe; sua presença não significa que o dado seja transmitido nem referenciado ao mundo AR.

O MPU6050 contém acelerômetro e giroscópio de três eixos; o magnetômetro mencionado na documentação é externo. Portanto, por inferência física, o conjunto isolado não mede heading absoluto: integrar o giroscópio exige uma referência inicial e admite deriva. A gravidade observada pelo acelerômetro não distingue uma rotação em torno do eixo vertical. Fonte: [especificação MPU-6000/MPU-6050, seções 5 e 7](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6000-Datasheet.pdf).

O ensaio inicial deve declarar orientação fixa ou calibrada. Para operação com giro livre do drone, será necessário escolher uma referência de orientação e transmitir a transformação correspondente. Aplicar a rotação atual do celular ao scanner sem medir a orientação relativa não resolve essa ausência.

O firmware já aplica compensação de pitch/roll às coordenadas. O aplicativo precisa respeitar essa convenção para não compensar a mesma inclinação duas vezes. A convenção de eixos deve ser testada inclinando e girando a montagem em movimentos conhecidos.

### Três âncoras e escolha de lado

As posições de âncoras configuradas formam um triângulo no plano Z=0, com largura de 154 mm e altura de aproximadamente 35,8 mm. Por geometria, pontos `(x, y, z)` e `(x, y, -z)` têm as mesmas distâncias a essas três âncoras. O cálculo escolhe o lado positivo; essa é uma hipótese de operação, não uma observação dos rádios.

É necessário confirmar se a tag sempre ficará desse lado. Caso contrário, a montagem ou as medições precisam fornecer informação adicional. A pequena separação entre âncoras também exige medir como erros de distância afetam a posição na faixa de uso pretendida; um filtro não cria informação geométrica ausente.

### Precisão e referência ARCore

O fabricante descreve o DWM1000 em ordem de precisão de 10 cm, e sua tabela de produto lista precisão de localização inferior a 15 cm em 2D e 30 cm em 3D. Esses valores do fabricante não são a precisão demonstrada desta montagem. Não há base para afirmar precisão milimétrica do conjunto. Fonte: [Qorvo DWM1000](https://www.qorvo.com/products/p/DWM1000).

Fixar a base ao celular exige conhecer deslocamento e rotação entre antenas e câmera. Se a base for fixa no ambiente, exige calibrar sua transformação para a origem AR. A transformação ARCore também pode mudar com rastreamento e relocalização; UWB relativo por si só não demonstra eliminação de drift global.

### Tempo de aquisição

O scanner e a base geram `timestampMs` com relógios locais independentes. Usar a pose mais recente ao receber um ponto pode associar instantes diferentes. Filas, agrupamento e transmissão acrescentam atraso variável. Um mapa estável com movimentos simultâneos exige estimar a relação entre relógios e associar cada ponto a uma pose válida naquele instante.

Correções para manter pontos já coletados no mundo evitam que a nuvem inteira acompanhe a tag, mas não substituem sincronização temporal. Para quantificar esse limite, comparar varredura com scanner/base parados e em movimento conhecido.

## UWB: biblioteca e medições reais

As duas cópias locais de `lib/DW1000/src/DW1000.h` expõem API e estado estáticos. Em `DW1000.cpp`, `select()` reinicializa o chip; `reselect()` troca o chip selecionado sem repetir essa inicialização. A alternância entre três rádios exige gerenciar também o estado compartilhado e a leitura dos eventos do rádio correto.

A revisão identificou reinicialização durante o ciclo de ranging e uso de tempo absoluto em uma API de atraso relativo. Essas correções estão sendo aplicadas no firmware, junto com validação de medidas e rejeição de esferas sem interseção. Mesmo após a correção lógica, o caminho de três rádios precisa ser ensaiado com hardware.

O ranging atual é de troca simples de mensagens; o desvio relativo entre relógios dos rádios e a calibração dos atrasos de antena continuam relevantes. Também é necessário medir taxa efetiva de ciclos completos. Uma espera de 20 ms ao final do ciclo não garante 50 posições/s, pois o tempo de aquisição é somado à espera.

## Significado do modo através de paredes

O uso consistente com os sensores é sobrepor no celular uma superfície que o scanner observou, mesmo quando essa superfície está atrás de uma parede em relação ao usuário. A câmera térmica observa a radiação das superfícies no seu campo de visão; ela não fornece uma imagem de objetos escondidos atrás de paredes opacas. Alterações de temperatura na superfície de uma parede são outro fenômeno e não equivalem a medir diretamente o objeto interno. Fonte: [FLIR: limites da imagem térmica](https://www.flir.com/discover/home-outdoor/can-thermal-imaging-see-through-walls/).

Transparência e realce térmico no renderizador devem ser avaliados como recursos de apresentação. O código não implementa a classificação geométrica completa de paredes proposta pelo plano, e as flags recebidas não comprovam segmentação planar.

## Metas e medições

As estimativas de 26%/4%/8% de CPU, GPU abaixo de 5%, 100 mil pontos a 60–120 FPS, redução geométrica de 95% e latência inferior a 10 ms não possuem resultados experimentais anexados que as demonstrem. Devem ser tratadas como hipóteses ou metas a revisar.

Para cada ensaio, registrar versão do firmware/aplicativo, dispositivos, condições de rede, número de pontos, duração, média e percentis relevantes. Medir separadamente aquisição, transporte, espera na fila, processamento e apresentação. Para precisão, usar alvos de posição e distância conhecidos, repetindo ensaios em várias orientações e distâncias.

## Alternativa USB sem mudar o transporte atual

A base usa `board = esp32dev` e ESP32-WROOM-32. Em placas dessa família, a conexão USB normalmente passa por uma ponte USB–UART; não se deve tratar a porta como CDC nativo do ESP32. A placa física precisa ser identificada para saber qual ponte está instalada. A documentação da [ESP32-DevKitC](https://documentation.espressif.com/esp-dev-kits/en/latest/esp32/esp32-devkitc/user_guide.html) descreve essa arquitetura.

Uma alternativa concreta é usar cabo/adaptador USB-C OTG com dados, confirmar que o Android enumera a placa e ler a serial pelo aplicativo com permissão de acesso USB. O Android disponibiliza APIs de USB Host; a biblioteca `usb-serial-for-android` implementa drivers para várias pontes, incluindo CP210x e CH34x, sem que isso identifique a ponte usada neste projeto. Fontes: [Android USB Host](https://developer.android.com/develop/connectivity/usb/host) e [repositório oficial usb-serial-for-android](https://github.com/mik3y/usb-serial-for-android).

No Unity, esse caminho exigiria integrar um leitor Android e entregar os registros UWB ao receptor espacial. Também seria necessário separar mensagens de diagnóstico da serial binária ou adotar enquadramento explícito, porque a base atualmente mistura logs e registros de posição em `Serial`. Um adaptador resolve apenas a conexão física; o software atual não lê os dados USB. Nenhuma mudança para USB foi implementada nesta revisão.

## Correções e validação desta revisão

Trabalho em andamento: recepção TCP/UDP e estado de conexão; inicialização e simulação; conservação de RGB e permanência da nuvem no espaço; parsing de comandos do firmware; validação matemática da trilateração; sequência de ranging UWB.

Os resultados finais de compilação e testes serão registrados após a conclusão das alterações. Não foi realizada validação física de sensores, rádios, montagem ou voo nesta revisão.

Decisões ainda necessárias: confirmar modelo/pinagem da placa montada, disposição fixa ou móvel da base, lado permitido do plano UWB, faixa de distância, liberdade de giro do scanner e objetivo inicial de demonstração em bancada ou voo.

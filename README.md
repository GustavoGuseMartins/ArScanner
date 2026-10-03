# AR Scanner

**Qualidade LiDAR de 03/10/2026:** [aviso de sinal fraco, intensidade e comparação de filtragem](docs/filtro-qualidade-lidar-2026-10-03.md). Firmware v12 preserva a qualidade no TCP/CSV; o aplicativo oferece rejeição opcional dos retornos avisados, desligada por padrão, sem novo corte por distância.

**Revisão de 03/10/2026:** [aproveitamento do hardware instalado](docs/aproveitamento-hardware-2026-10-03.md) e [integração e limites da GY-25](docs/plano-gy25-2026-10-03.md). Diagnóstico v11 separa identidade, leitura, calibração em repouso e aplicação de inclinação. A direção manual que funcionou permanece; a montagem da IMU e seu modo I²C ainda precisam de validação física.

**Revisão de 02/10/2026:** [análise do vídeo, perfis e tempo de calibração](docs/analise-testes-2026-10-02.md). Preserva o progresso espacial durante ausência temporária do estado do scanner, mantém a captura dependente de estado recente e restaura o slider de opacidade no painel normal. [Avaliação de validação visual de X/Z e montagem](docs/avaliacao-posicao-automatica-2026-10-02.md), usando as peças atuais e mantendo a direção manual.

**Recuperação do fluxo após os últimos testes:** [causas dos bloqueios, fixação explícita do eixo e aquisição térmica por subpáginas](docs/recuperacao-fluxo-2026-09-30.md). O reconhecimento da tampa fica experimental e desligado por padrão. A precisão física de 10 cm / 10° continua a verificar em bancada.

A [auditoria das funções do aplicativo](docs/auditoria-funcoes-app-2026-09-30.md) registra cada controle, seu uso e a decisão de manter, recolher no diagnóstico ou retirar.

**Implementação de 30/09/2026:** [confirmação da posição, direção pela tampa original do LiDAR e zero do pan salvo na flash](docs/implementacao-pose-pan-2026-09-30.md), baseada na [análise das três tentativas filmadas](docs/analise-testes-2026-09-30.md). A precisão de 10 cm / 10° ainda depende do ensaio físico.

**Implementação de 29/09/2026:** [térmica com lado ajustável, pontos de 6,25 mm com LOD e associação temporal UWB/AR](docs/implementacao-fusao-2026-09-29.md). O [ensaio térmico no equipamento](docs/calibracao-termica-2026-09-29.md) ainda deve confirmar o perfil óptico; o scanner móvel continua suspenso.

Protótipo de TCC para visualizar em realidade aumentada uma nuvem de pontos de LiDAR, com temperatura e suporte a cor RGB, coletada por um scanner ESP32-S3. A proposta prevê o scanner instalado em um drone e uma base com três rádios UWB para estimar sua posição relativa ao visualizador Android.

O scanner envia pontos por **Wi-Fi/TCP** e a base UWB envia alcances ao Android por **USB-C serial**. O repositório contém firmware, aplicativo Unity e simulador; isso não representa validação do conjunto físico em voo. Consulte o [fluxo atual e os próximos ensaios](docs/fluxo-medicao-uwb-pontos.md) para a divisão do processamento.

**Revisão de pose de 27/09/2026:** [análise, correções e limites da fusão UWB/AR/GY-25](docs/pose-fusao-2026-09-27.md). Corrige congelamento da pose aproximada, referências antigas e leituras inválidas da IMU; captura física exige scanner parado e direção alinhada. Movimento contínuo com reconstrução ainda depende de temporização e orientação completas.

**Atualização de bancada de 23/09/2026:** [diagnóstico das novas capturas e roteiro de teste](docs/diagnostico-novas-capturas.md), com teste de eixo parado, alinhamento da origem AR, captura de raios, diagnóstico UWB e perfil isolado da câmera. APK compilado: `Builds/ScannerAR-diagnostico-20260923.apk`. Validação física ainda pendente.

**Calibração da montagem real:** consulte o [roteiro atualizado](docs/calibracao-montagem-real.md). O código atual usa LiDAR vertical no plano XY, zero para cima e origem 50 mm acima/90 mm à frente do pan. O HUD mostra geometria/MPU e exporta CSV; a nuvem usa cor sólida. O APK citado acima é de uma etapa anterior e não contém estas alterações. Firmware e C# foram compilados; sentidos físicos e eixos da MPU ainda precisam de teste.

**Correção da interface:** `CenaViewer` agora contém o controlador e o HUD diretamente, sem depender do script UDP removido. Inicialização verificada em Play Mode. APK atualizado: `Builds/ScannerAR-interface-corrigida.apk`.

## Arquitetura implementada

| Componente | Responsabilidade | Comunicação atual |
| --- | --- | --- |
| Scanner ESP32-S3 | LiDAR, motor de passo, MPU6050, MLX90640 e preparação de pontos | Cria a rede `ArScanner_Net`; servidor TCP `8888` para pontos e comandos |
| Câmeras do scanner | Prévia RGB e matriz térmica | HTTP `8889`: `/rgb` e `/thermal`; RGB depende de câmera configurada |
| Base ESP32-WROOM-32 | Leitura DS-TWR dos três DWM1000, diagnóstico e trilateração experimental | USB-C serial CP210x a 115200 baud para o celular |
| Aplicativo Android | Recepção USB, referência AR, transformação espacial, voxelização, cores e visualização | Cliente TCP do scanner e USB da base |

O IP padrão do scanner é `192.168.4.1`. A senha de desenvolvimento definida no firmware é `scanner123`; confira os arquivos `config.h` se usar outro firmware.

Cada ponto ocupa 28 bytes: XYZ em milímetros, temperatura, RGB, flags, pitch, roll e timestamp. O TCP transmite uma sequência desses registros, agrupados em escritas de até 15 pontos, sem cabeçalho de lote. O pacote UWB também ocupa 28 bytes, mas tem outro formato: XYZ em metros, três distâncias e timestamp. Consulte as estruturas em `firmware/scanner/include/config.h`, `firmware/viewer/include/config.h` e `Assets/Scripts/Network/`.

A base envia mensagens JSON de diagnóstico por USB mesmo sem posição válida. Na serial, posições aceitas usam uma linha `@UWB28:` seguida de 56 caracteres hexadecimais; mensagens JSON e logs têm linhas próprias. O Android lê a base diretamente via USB. A bridge `firmware/serial_to_udp_bridge.py` permanece apenas como opção de diagnóstico no computador. A posição automática no AR é recusada quando a calibração, a ambiguidade ou a incerteza geométrica não permitem localizar o scanner com confiança.

## Abrir e executar no Unity

Versões registradas no projeto:

- Unity **6000.5.4f1**.
- Universal Render Pipeline **17.5.0**.
- AR Foundation e ARCore XR Plugin **6.5.0**.
- Input System **1.19.0**.
- Android: mínimo configurado API 26, ARM64 e IL2CPP. O aparelho precisa ser compatível com ARCore.

Instale pelo Unity Hub o editor indicado e o módulo Android Build Support com SDK, NDK e OpenJDK. Abra a pasta raiz do projeto e aguarde a importação de assets e pacotes.

### Simulação sem placas

1. Abra `Assets/Scenes/CenaViewer.unity` no Editor e entre em Play.
2. Em **Diagnóstico e ajustes > Testes de bancada e experimentos**, selecione **Iniciar simulação no editor**.
3. Use o HUD para visualizar a geometria, limpar a nuvem ou exportar PLY. Ajustes de montagem e apresentação ficam no diagnóstico; leituras de sensores e CSV do scanner exigem hardware conectado.
4. No Editor, navegue segurando o botão direito do mouse, com WASD para deslocamento, Q/E para altura e Shift para acelerar.

A simulação exercita o aplicativo com pontos e posição sintéticos. Ela não valida sensores, rádio UWB, calibração física nem precisão espacial.

### Compilar o aplicativo Android

Use **ArScanner > Build Android APK**. O comando inclui `MenuViewer` e `CenaViewer`, nessa ordem, e gera `Builds/ScannerAR.apk`. O menu **ArScanner > 4. Atualizar Build Settings (MenuViewer + CenaViewer)** aplica as mesmas cenas para compilações manuais.

O menu **ArScanner > 3. Injetar ArScanner na Cena Aberta** é uma ferramenta de montagem de cenas e só é necessário se a cena ainda não contiver o controlador. Salve a cena após qualquer montagem intencional.

### Usar com hardware

1. Confira a placa, pinagem, alimentação e geometrias nos `config.h` antes de gravar o firmware correspondente.
2. Ligue o scanner e conecte o Android à rede `ArScanner_Net`. Conecte a base UWB ao celular por USB-C.
3. Abra o aplicativo e permita acesso à câmera e à base USB. **ABRIR SCANNER AR** fica disponível com o scanner conectado por Wi-Fi. Sem medidas UWB, use a marcação manual do eixo no apoio.
4. Para posição automática, use o perfil UWB salvo e três distâncias recentes, observe o apoio e percorra vistas diferentes com o celular. Quando a posição estiver estável, toque em **Fixar eixo UWB estimado**. Três distâncias isoladas não comprovam uma posição precisa com a PCB compacta.
5. Confira o zero do pan. Defina a direção no AR apontando para um ponto livre do apoio na direção da frente do LiDAR. Use **Iniciar / retomar captura** para solicitar a varredura; use **Pausar captura** para interrompê-la.

As correções de software desta revisão estão em validação. O uso físico depende especialmente dos ensaios de ranging UWB, orientação e calibração descritos no relatório técnico.

Atualização de integração (26/09/2026): [APK, comunicação, parâmetros e roteiro de teste](docs/integracao-diagnostico.md). Prioridade UWB + térmica; RGB desativada.

**Revisão UWB de 26/09/2026:** [geometria medida da PCB, datasheet DWM1000 e roteiro de calibração](docs/uwb-calibracao-2026-09-26.md). O cálculo usa a base de 175 mm e os lados de 100 mm informados pelo autor; posições de baixa confiança são suspensas e o scanner parado pode ser marcado no AR. APK de teste: `Builds/ScannerAR-uwb-calibracao-20260926.apk`, instalado no Samsung SM-S916B. O ensaio físico de alcance continua pendente.

**Diagnóstico UWB mais recente:** `Builds/ScannerAR-uwb-diagnostico-20260926.apk`, instalado no Samsung SM-S916B em 26/09/2026 e aberto sem erro de inicialização. Esta revisão descarta automaticamente perfis antigos/provisórios, preserva a marcação manual AR e grava as séries UWB brutas e corrigidas em CSV quando a base USB fornece dados. A gravação com os rádios conectados ainda precisa ser verificada no ensaio físico. Veja [o fluxo de medição e o roteiro de ensaio](docs/fluxo-medicao-uwb-pontos.md).

## Firmware

Os dois projetos usam PlatformIO com framework Arduino. Execute a partir da raiz:

```powershell
pio run --project-dir firmware/scanner --environment esp32s3
pio run --project-dir firmware/viewer --environment esp32dev
```

Para gravar, acrescente `--target upload` ao comando da placa correspondente. Os monitores seriais usam 115200 baud. Os ambientes ficam definidos em `firmware/scanner/platformio.ini` e `firmware/viewer/platformio.ini`.

## Limites atuais

- A OV2640 está desativada no perfil normal. O perfil `esp32s3_rgb_test` testa a pinagem candidata da S3-CAM com a tag UWB desativada: SDA4/SCL5 da câmera coincidem com MOSI4/MISO5 da PCB. Uso simultâneo depende de confirmação e mudança física das ligações. A interface inclui prévia HTTP térmica e mostra o motivo de indisponibilidade da RGB.
- Três âncoras coplanares exigem escolher um lado do plano; a posição UWB não informa a orientação do scanner. Falta definir a referência de yaw e sincronizar pose e pontos entre dispositivos.
- O filtro da base é uma suavização Kalman escalar; não é o EKF previsto no plano. A redução de pontos no aplicativo não é segmentação RANSAC com geração de quads.
- O modo térmico translúcido apresenta dados observados pelo scanner. Não mede objetos através de paredes opacas.
- Precisão, taxa de aquisição, latência e FPS precisam ser medidos no conjunto físico. Os números dos planos são metas, não resultados de benchmark.

## Organização e histórico

| Caminho | Conteúdo |
| --- | --- |
| `Assets/Scripts/` | Rede, simulador, transformação espacial, renderização e HUD atuais |
| `Assets/Editor/` | Montagem de cenas e compilação Android |
| `firmware/scanner/` | Firmware do scanner ESP32-S3 |
| `firmware/viewer/` | Firmware da base UWB ESP32-WROOM-32 |
| `docs/revisao-tecnica.md` | Diagnóstico, limites físicos e pendências verificáveis |
| `tcc.md`, `implementation_plan.md`, `gemini-code-1787612425528.md` | Especificações e planos históricos; contêm divergências registradas na revisão |

As cenas `MenuScanner`/`CenaScanner` e scripts do protótipo anterior de dois celulares foram preservados. Seu fluxo UDP de scanner ARCore é legado e não faz parte do APK atual do visualizador.

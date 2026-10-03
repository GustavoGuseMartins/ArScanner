# Posição, direção e movimento do scanner — revisão de 27/09/2026

## Conclusão

O conjunto atual permite medir alcances e acompanhar movimento relativo com limitações. Não há evidência de que consiga reconstrução 3D precisa durante movimento livre. O código anterior combinava um estimador para alvo parado, uma posição aproximada de baixa confiança e uma direção manual. Isso não equivale a um sistema de fusão de pose em movimento.

Para o equipamento existente, a opção utilizável agora é parar para cada varredura, localizar o eixo e alinhar a direção. O modo experimental de deslocamento acompanha apenas o eixo, com o pan parado; não autoriza acumular pontos com tempos ainda incompatíveis. A captura contínua em movimento permanece uma etapa de desenvolvimento, não uma funcionalidade validada nesta revisão.

Confirmado pelo usuário: três DWM1000 na placa solidária ao celular; uma tag DWM1000 e um GY-25/MPU6050 na PCB vertical da cabeça, girando juntos; ligação da IMU por SDA/SCL no mesmo barramento da MLX90640. Não foi confirmado o fechamento do seletor I²C do GY-25. Marcador visual não foi adotado, por preferência do usuário.

## Onde a implementação falhava

| Evidência no código | Consequência |
|---|---|
| `UwbArMultiviewEstimator` reúne alcances de diferentes posições AR supondo uma única tag imóvel | Se o scanner se desloca, mistura posições físicas diferentes; mover o telefone e pausar leva tempo por construção |
| A pose aproximada atribuía `hasMultiviewPose = true`; na próxima leitura o retorno de pose congelada vinha antes da atualização aproximada | O eixo podia ficar preso já na primeira posição aproximada |
| Trocar montagem/modo limpava somente o estimador instantâneo | Amostras multivista e sinalizadores da posição anterior podiam reaparecer |
| Retornos de congelamento vinham antes da validação do AR | Perda da referência do celular podia manter uma autorização de captura antiga |
| O fallback aproximado aceitava a classe `Ambiguous` | Uma solução espelhada ainda não resolvida podia virar posição visível |
| Direção do scanner não era condição de captura; o indicador não somava o pan atual | O eixo podia sugerir orientação válida sem alinhamento e não acompanhar a cabeça |
| Três alcances positivos bastavam no telefone, mesmo sem validar a janela temporal do ciclo | Uma trinca longa demais podia ser consumida mesmo tendo sido recusada pelo firmware |
| A biblioteca da MPU não verificava leituras I²C completas; o chamador atualizava o tempo de leitura incondicionalmente | Falhas do barramento podiam parecer dados recentes |

A cena conserva uma correção local de yaw de **200°**, estabelecida nas iterações anteriores. Isso é uma correção de visualização, não a direção mundial do scanner em qualquer nova sessão AR. O alinhamento retira essa correção da rotação da raiz para que a direção final coincida com a direção observada. A revisão preserva esse ajuste e exige a referência inicial, em vez de inventar outro ângulo fixo.

O renderer já mantém pontos acumulados no mundo, o que é correto para deslocamento. Porém aplica aos pontos recebidos a pose disponível no processamento, sem consultar a pose no `timestampMs` de cada ponto. Portanto ainda falta corrigir a deformação causada pelo movimento durante a aquisição e pelo atraso de rede.

## Limitações físicas e calibração

As coordenadas em uso representam um triângulo de 175 mm de base e apenas 48,4 mm de altura. A alguns metros, os três alcances mudam quase da mesma maneira. Pequenas diferenças de erro de rádio geram grandes diferenças de direção e altura. Um resíduo pequeno não comprova boa posição: três esferas podem se intersectar com geometria ruim ou oferecer soluções refletidas.

O ensaio anterior de 27/09, documentado em `diagnostics/20260927/analise.md`, registrou 230 de 479 trincas corrigidas excedendo inclusive a tolerância de incompatibilidade de 25 cm. Esses dados são históricos; esta revisão não fez nova medição física. A solução aproximada continua expondo incerteza que pode chegar a metros, e não deve ser confundida com precisão centimétrica.

Uma tag observa a posição de um ponto, não a orientação completa do corpo naquele instante. O acelerômetro fornece uma referência de gravidade; o giroscópio fornece variações angulares com deriva. A MPU6050 não oferece uma referência horizontal absoluta. Na placa vertical, integrar `gyroZ` tampouco equivale automaticamente a girar em torno do Y do mundo.

Em princípio, a órbita conhecida de uma tag excêntrica, medidas ao longo do tempo e movimento controlado podem acrescentar informação de orientação. Aqui a excentricidade horizontal configurada é de apenas 20 mm e os erros observados são muito maiores. Não há suporte para prometer autoalinhamento rápido e preciso desse modo.

Calibração de montagem e atraso de antena pode ser feita uma vez por conjunto estável e reutilizada. Bias de giroscópio depende das condições e requer observações em repouso. A referência inicial do scanner no mundo AR precisa ser observada novamente quando sua posição/direção muda. Ajustar continuamente os rádios usando a própria posição UWB estimada como verdade criaria uma realimentação de erros.

## O que foi implementado

- A pose aproximada continua recebendo novas posições; somente a solução multivista estacionária é conservada como tal.
- A declaração de scanner parado ficou explícita. Há controle para acompanhar deslocamento experimentalmente e outro para refazer posição/direção após reposicionamento.
- Mudanças de modo/montagem e perda de AR invalidam o histórico apropriado. A perda de estado do pan suspende a posição automática, em vez de presumir pan zero.
- A captura física exige posição, AR ativo, scanner declarado parado e direção alinhada. O estado experimental móvel não grava pontos. A simulação mantém seu caminho próprio.
- O indicador dos eixos acompanha o pan informado pelo scanner. O pan não é aplicado novamente à nuvem, pois já foi usado no firmware.
- Ao alinhar yaw, a conversão tag→eixo é recalculada com a nova direção. Ajustar yaw da nuvem invalida o alinhamento anterior.
- O cálculo multivista só é refeito quando há uma nova amostra aceita; não se repete a mesma otimização quando a posição de coleta já está cheia.
- Diagnóstico UWB versão 2 inclui `t1Ms`, `t2Ms`, `t3Ms` e `cycleValid`. Os tempos marcam o fim das trocas no relógio da base. O aplicativo aceita também v1, recusa ciclos v2 incoerentes e registra os novos campos no CSV. **Isso ainda não sincroniza os três dispositivos.**
- Leitura da MPU passou a verificar escrita de configuração, identidade e os 14 bytes do conjunto de dados. Falhas não atualizam a idade; falhas repetidas levam à reinicialização já existente quando parado.
- Bias da MPU é calculado em janela de 3 s, com rejeição de movimento evidente e dispersão excessiva. Rotação lenta constante pode se confundir com bias: a instrução de manter imóvel continua necessária.
- Configuração da MPU: ±2 g, ±500°/s, filtro de 44/42 Hz, taxa interna de 100 Hz. Essa taxa **não é a taxa efetiva garantida de leitura**, pois a tarefa ainda compartilha tempo e barramento com a térmica.
- Intervalos longos/falhos não são preenchidos com uma única velocidade angular. `imuSampleIntervalUs` e `imuIntegrationGaps` tornam visíveis essas lacunas. Ângulos continuam diagnósticos; `IMU_APPLY_TILT` permanece desativado até medir a montagem.

## Arquitetura recomendada para captura em movimento

1. **Referência inicial:** sem marcador, usar alinhamento físico conhecido com o celular ou uma observação independente da direção. Uma base/encaixe de inicialização pode tornar isso rápido e repetível, mas seria uma alteração mecânica. Homing do motor resolve o zero mecânico, não o yaw mundial.
2. **IMU contínua:** mapear a rotação rígida sensor→cabeça; obter amostras temporizadas por FIFO/data-ready e impedir que a leitura térmica apague trechos da trajetória. Não basta criar duas tarefas concorrentes no mesmo I²C sem coordenar o barramento.
3. **Tempo comum:** alinhar relógios do scanner, base e celular; transmitir IMU, pan e pontos com instantes de aquisição; guardar histórico/interpolação da pose AR. Tempos de recepção e diagnóstico HTTP de aproximadamente 1 Hz não servem para essa fusão.
4. **Fusão:** manter posição, velocidade, orientação e biases; usar cada alcance bruto corrigido como observação, com sua âncora na pose AR correspondente. Rejeitar reflexões/outliers e manter incerteza nas direções pouco observáveis, em vez de alimentar o filtro com uma trilateração instável tratada como verdade.
5. **Geometria:** para melhorar a posição usando UWB, ampliar de forma relevante a separação entre antenas. Uma quarta âncora fora do plano ajuda a ambiguidade, mas outra antena quase no mesmo lugar não resolve a amplificação de erro. Outra tag no scanner pode acrescentar direção, desde que sua separação seja grande frente ao erro de alcance.
6. **Correção independente:** para manter precisão no longo prazo sem marcador, investigar odometria visual ou LiDAR com correspondência de superfícies. No LiDAR que faz varredura mecânica, isso exige corrigir movimento durante a aquisição. Magnetômetro exige avaliação perto do motor e metal e, sozinho, não corrige posição.

Modelo de observação desejado: `alcance_i(t) = |posição_eixo(t) + R_base(t) R_pan(t) offset_tag - posição_âncora_i(t)|`. Usar o giro da cabeça como se fosse giro da base ou esquecer o offset da tag contamina a estimativa.

Essa arquitetura reaproveita os sensores, mas requer implementação e ensaios adicionais. Não foi habilitada uma suposta fusão completa usando eixos desconhecidos e amostras sem sincronização.

## Verificação e ensaio físico

Os testes de software e builds estão registrados junto aos diagnósticos desta data. Conferir o resultado final em `pose-build.log` e `pose-playmode.log`. O teste nativo `imu_sample_math_test.cpp` verifica decodificação com sinal, calibração com placa vertical e rejeição de movimento/vibração. Testes Unity cobrem posição sintética, espelhamento, movimento temporal, offset da tag, multivista e replay histórico, coerência temporal incluindo rollover, limpeza de histórico e restrições de captura.

O teste `imu_driver_test.cpp` executa o driver real contra um barramento simulado: leitura truncada, descarte dos bytes restantes, preservação do último dado, lacunas, reinicialização e calibração durante rotação. Ambos os testes nativos passaram. Os três testes da bridge serial passaram com suporte a diagnóstico v1/v2. A integração da cena passou em Play Mode; os dois firmwares compilaram. Os testes da nova fórmula de yaw incluem tag deslocada lateralmente e pan não nulo.

APK desta revisão: `Builds/ScannerAR-pose-validada-20260927.apk`. Instalar o APK novo antes de atualizar o firmware do visualizador: o aplicativo antigo só entende diagnóstico v1, enquanto o novo aceita v1 e v2. O nome do APK se refere à validação de software, não à precisão física. Firmware do scanner e do visualizador foram compilados por seus respectivos projetos PlatformIO, sem upload.

O scanner não respondeu a HTTP nesta máquina durante a revisão. Não houve instalação, gravação de firmware, medição de latência real ou confirmação de precisão física. Compilação e testes sintéticos não substituem isso.

Ensaio recomendado: conferir seletor I²C do GY-25; ligar com cabeça imóvel; verificar aceleração próxima de 1 g e gyro próximo de zero; medir quais canais respondem aos giros conhecidos. Com pan parado, colocar o scanner em posições físicas medidas, alinhar a direção e registrar erro/latência. Separar ensaios de movimento do telefone, translação do scanner e giro do pan. Medir também a taxa e as lacunas da IMU com térmica ligada. Só então estabelecer limites aceitáveis para habilitar captura móvel.

## Referências primárias

- [Manual GY-25 do fabricante](https://www.gysensor.cn/wp-content/uploads/GY-25%E5%80%BE%E6%96%9C%E8%A7%92%E5%BA%A6%E4%BC%A0%E6%84%9F%E5%99%A8%E6%A8%A1%E5%9D%97%E4%BD%BF%E7%94%A8%E6%89%8B%E5%86%8C2.pdf): seleção física de I²C, acesso à MPU6050 e ausência de magnetômetro.
- [Mapa de registradores MPU6050](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6000-Register-Map1.pdf): configuração e interpretação das amostras.
- [Qorvo DWM1000](https://www.qorvo.com/products/p/DWM1000): documentação do rádio e nota APS014 sobre calibração de atraso de antena.

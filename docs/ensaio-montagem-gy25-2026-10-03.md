# Ensaio físico da GY-25 — 03/10/2026

## Resultado

A GY-25 respondeu às mudanças de posição e continuou compartilhando o I²C com a câmera térmica. Os movimentos conhecidos sustentam a correspondência `headRH = (Ysensor, −Xsensor, Zsensor)` identificada no vídeo e no diagrama. O ensaio confirma canais e sentidos, sem certificar precisão angular nem ativar correção da nuvem.

Há duas limitações medidas antes de usar orientação automaticamente: a norma de aceleração varia entre poses estáveis, e o giroscópio apresenta velocidade residual mesmo com o conjunto parado. O estado `bias=1` significa calibração do giroscópio na inicialização, não calibração completa do acelerômetro nem ausência de deriva posterior.

## Coleta e confirmação física

Scanner conectado por USB em COM6, VID/PID `1A86:55D3`, serial `5C39002172`. A primeira coleta ocorreu às 14:16 e a última às 14:29, horário de Brasília; os arquivos preservam timestamps UTC. Cada pose foi confirmada pelo usuário antes da coleta. A comunicação serial foi somente leitura, com DTR/RTS desativados antes da abertura. Não foram enviados comandos, movimentados motores, gravados firmwares ou instalados APKs.

Foram preservados 24 snapshots em cinco arquivos JSON e seus logs. São diagnósticos a cada aproximadamente cinco segundos, suficientes para comparar poses mantidas; não são amostras adequadas para calibrar bias fino, sincronizar pontos LiDAR ou validar acompanhamento do pan.

| Pose confirmada pelo operador | Amostras | Média de aceleração XYZ (g) | Resultado |
| --- | ---: | --- | --- |
| Apoiado na posição normal | 6 | −0,9332 / −0,0195 / +0,2712 | Predomínio de −X |
| Topo em direção ao LiDAR | 4 | −0,9365 / −0,0205 / +0,2608 | Separação de apenas 0,65°; inconclusivo |
| Face do CI voltada para o teto | 5 | +0,0596 / −0,0506 / +1,0580 | +Z confirmado saindo da face do CI |
| Topo para a direita, olhando pelo lado PCB/ESP32 | 4 de 5 | −0,8190 / −0,5148 / +0,1508 | Y diminuiu claramente; sentido lateral confirmado |
| Retorno ao apoio normal | 4 | −0,9385 / −0,0203 / +0,2540 | Separação de 1,06° da referência inicial |

Na pose lateral, o snapshot de `17:27:36.009530 UTC` apresentou norma de aceleração de 1,177 g e gyro de 38,43°/s, indicando movimento. Foi excluído somente da estatística usada para identificar a direção; permanece no arquivo bruto. A regra operacional da análise é norma de aceleração entre 0,9 e 1,1 g e norma do gyro até 2,5°/s. Isso não altera nenhum filtro LiDAR e não é uma especificação de precisão do fabricante.

As quatro amostras laterais selecionadas também variaram um pouco: desvio padrão máximo de 0,036 g. Todas tiveram Y negativo significativo, mas não descrevem uma pose fixa com ângulo certificado. A diferença angular da seleção lateral em relação à inicial foi aproximadamente 31,05°. A primeira tentativa para frente não foi usada para aprovar ou reprovar o mapa: não foi possível determinar a causa da pequena separação a partir desses registros.

## Correspondência dos eixos

A direita é definida olhando pelo lado da PCB/ESP32; a traseira é o lado exposto do CI, oposto ao LiDAR. Vídeo, pose normal, face do CI para cima e inclinação lateral são coerentes com:

| Sensor positivo | Direção na cabeça |
| --- | --- |
| +X | Para baixo |
| +Y | Para a direita |
| +Z | Para trás, saindo da face do CI |

Esse resultado sustenta os índices e sinais do mapa ortogonal proposto. Não mede eventuais pequenos desalinhamentos da placa ou do sensor em relação ao eixo mecânico. Para Unity, aceleração é `(Ysensor, −Xsensor, −Zsensor)` e gyro é `(−Ysensor, Xsensor, Zsensor)`, considerando a diferença de convenção entre vetores polares e axiais.

O giro vertical corresponde geometricamente principalmente ao gyroX quando o scanner está nivelado. O `angleZ` atual integra o Z do sensor e não representa yaw vertical nessa montagem. Este ensaio não comandou pan; o acompanhamento dinâmico e sua correspondência com o motor continuam sem validação física.

## Saúde do barramento compartilhado

- MPU: identidade decimal 104 (`0x68`), pronta e bias concluído em todas as amostras; erros de leitura 0→0, inicializações 6→6, idade observada 0–52 ms e lacunas de integração 1→1. A inicialização e a lacuna existentes antecedem a coleta; não foram novas falhas deste ensaio.
- Térmica: contador de quadros 699→3725; erros I²C 0→0, timeouts 2→2 e overruns 2→2. Leituras de 51–53 ms nos snapshots.
- A térmica continua entregando quadros parciais com 18 pixels mascarados e aviso de calibração −4, condição já registrada anteriormente. O funcionamento conjunto foi observado; isso não certifica que todos os pixels térmicos estejam válidos.

O firmware lê os registradores de aceleração/gyro diretamente da MPU6050, e o diagnóstico serial imprime a leitura corrente sem transformação ou compensação da aceleração. A mudança clara na pose com o CI para cima afasta a hipótese de vetor constantemente congelado neste ensaio. A idade informada representa a última transação aceita; o driver ainda não conta conversões novas por `DATA_RDY` nem relê a configuração após escrevê-la.

## Limitações relevantes para a calibração automática

**Acelerômetro:** a norma média foi 0,972 g no apoio inicial e 1,061 g com a face do CI para cima, diferença de aproximadamente 0,089 g. Duas poses sem referência angular externa não isolam bias, escala ou outra causa. A leitura inferida como pitch de aproximadamente 16° na posição inicial não pode ser tratada como inclinação mecânica certificada. Precisamos conferir bias/escala em poses adicionais antes de usar esses valores para corrigir pontos.

**Giroscópio:** com o scanner apoiado e parado, gyroX médio foi −0,265°/s no início e −0,533°/s no retorno; gyroZ foi +0,080 e +0,175°/s. São resíduos após a correção de bias atual. Integrá-los acumularia erro de orientação. Não foi medida temperatura nem executada nova calibração, portanto não se atribui a causa a aquecimento ou defeito. Não estimar uma correção de precisão a partir destes poucos valores arredondados: utilizar a aquisição local e uma janela de repouso, e validar a deriva após a recalibração.

**Posição e direção inicial:** a MPU6050 fornece aceleração e giro. A correspondência dos eixos permite avaliar repouso/inclinação e preparar orientação relativa; não acrescenta uma referência absoluta de X/Z no ambiente ou da direção inicial no AR. UWB/AR e a referência manual continuam necessários no fluxo atual. Medir o giro relativo também exige descontar o pan e tratar perdas de amostras.

## Próximo uso justificado pelos resultados

A menor integração útil é apresentar repouso e inclinação com o mapa da cabeça explicitamente identificado. Para acompanhar rotação, primeiro verificar o bias em repouso com a aquisição local e medir deriva; depois comparar um giro conhecido com o pan. Para compensar a nuvem, também conferir o acelerômetro, composição pan/tilt, reflexão vertical no aplicativo e sincronização das amostras com LiDAR.

`IMU_APPLY_TILT` permanece `false`. Este teste não modificou a geometria, a pose UWB ou o alinhamento manual que funcionaram. Não foi realizado escaneamento LiDAR nem comparação entre condições da alimentação principal.

## Evidência preservada

[Análise reproduzível e seleção de amostras](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/imu-mount-validation/analysis.json), [captura inicial](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/imu-mount-validation/normal.json), [CI para cima](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/imu-mount-validation/ci_up.json), [inclinação lateral](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/imu-mount-validation/right.json) e [retorno](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/imu-mount-validation/return_normal.json). Os logs correspondentes preservam os diagnósticos térmicos.

A [análise do vídeo e do diagrama](C:/Users/aaata/Projetos/tcc/ArScanner/docs/montagem-gy25-video-2026-10-03.md) contém a evidência visual e as fontes do fabricante. O código usado para resumir estas observações não acessa nenhum dispositivo.

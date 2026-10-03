# Calibração da montagem real — 23/09/2026

## Montagem confirmada

- Coordenadas do scanner: X para a direita, Y para cima, Z do eixo de pan em direção ao LiDAR. Metros no cálculo e milímetros no pacote TCP/CSV.
- Centro óptico: `(0, 0.050, 0.090)` m, antes da rotação do pan.
- LiDAR vertical, zero do protocolo para cima. O feixe horizontal atravessa a direção dos 90 mm: plano local XY, portanto `LIDAR_MOUNT_YAW_DEG = 90`, `LIDAR_ZERO_DEG = 90`.
- `LIDAR_ANGLE_SIGN = +1` é **provisório**: 90° do protocolo aponta para -X; 270° aponta para +X. Se o teste físico mostrar o inverso, trocar somente o sinal para -1.
- Pan usa a convenção positiva horária vista de cima. O movimento inicial informado é anti-horário; `STEPPER_PAN_SIGN = -1` converte os pulsos para essa convenção. Não houve inversão elétrica do motor. Ping-pong mantém os dois sentidos.
- MPU atrás do LiDAR, na cabeça móvel. Essa posição não revela os eixos/sinais. `IMU_APPLY_TILT = false` até a calibração.

O zero do pan é a posição na partida; não há homing nem encoder. A altura de 50 mm é relativa à origem do pan, não ao chão. No app, a altura da origem sobre o apoio é um ajuste separado. O deslocamento da antena UWB continua nominal, ainda sem calibração física.

## De onde vem o ângulo

O parser usa o índice do pacote: `ângulo = (índice - 0xA0) * 4 + amostra`, com quatro amostras por pacote e 90 pacotes por volta. PWM controla a velocidade do motor DC; não fornece a posição angular. O protocolo do sensor já identifica as amostras angulares. A implementação não pressupõe qual mecanismo interno mede essa posição no exemplar instalado.

Referência primária do formato adotado: [Roborock — LDS serial data](https://github.com/Roborock-OpenSource/Cullinan#lds-serial-data). Confirmar o comportamento físico do sensor continua necessário.

## Diagnóstico disponível

- `http://192.168.4.1:8889/geometry`: parâmetros ativos, orientação do plano, sinais, offsets, redução, microsteps e mapeamento da MPU.
- `/status`: `imuRaw` com aceleração em g, giroscópio em °/s e ângulos da biblioteca em graus. Os dados são copiados juntos pela tarefa da MPU; `imuAgeMs` informa a idade. Não são registradores ADC: o giroscópio já inclui a correção de bias da biblioteca.
- `/scan.csv`: últimas 1024 amostras aceitas para projeção. Inclui ângulo bruto, distância, pan já convertido à convenção CW+, ângulo corrigido do LiDAR, pitch/roll realmente aplicados e XYZ finais em mm, antes dos ajustes AR/Unity. `queued=1` significa inserido na fila TCP, não confirmação de entrega ao celular. Amostras rejeitadas por pose não entram no CSV; veja `poseDrops`.
- `CALIBRATION_DIAGNOSTIC_MODE` habilita o registro CSV. O pacote TCP continua com 28 bytes, mantendo compatibilidade.
- App: seção **CALIBRAÇÃO**, atualização de geometria e **Salvar diagnóstico CSV**. Arquivos ficam em `Application.persistentDataPath/Scans`; o caminho completo aparece na tela. A exportação PLY continua disponível.
- `tools/capture_scan.py` preserva todas as colunas CSV e salva a geometria em `.geometry.json`. Capturas repetidas podem perder amostras entre snapshots; não são um registro contínuo garantido.

A nuvem usa cor sólida, tamanho uniforme e teste de profundidade. Os dados térmicos/RGB do protocolo permanecem disponíveis no PLY, mas não alteram a visualização de calibração. O app não solicita imagens das câmeras.

## Teste físico

1. Ligue o scanner imóvel e aguarde a calibração inicial do giroscópio. Coloque o pan em uma posição física marcada **antes de ligar**, pois essa posição será o zero relativo.
2. Deixe os offsets Pitch/Yaw do app em zero. Use prévia local enquanto o UWB não fornecer posições válidas. Marque a origem do scanner e ajuste a altura do eixo sobre o apoio.
3. Selecione **Teste 2D: parar eixo externo**, inicie o scan e limpe os pontos após a parada. A fatia deve ser vertical e transversal à linha eixo–LiDAR.
4. Posicione um alvo em um dos lados conhecidos da cabeça. Salve o CSV e compare as leituras próximas de 90°/270°. Com o sinal atual, 90° deve estar à esquerda (-X), olhando do eixo na direção do LiDAR; 270° à direita (+X). Se estiver invertido, use `LIDAR_ANGLE_SIGN = -1` e recompile.
5. Retome o pan em baixa velocidade. Compare uma marca física de 90° com a telemetria. Movimento anti-horário deve produzir ângulo negativo; horário, positivo. **Girar à mão não atualiza a telemetria** e invalida a referência dos pulsos. O giro estimado depende também da redução e dos microsteps reais.
6. Pare os motores e observe os três eixos de aceleração. Em repouso, o módulo do vetor deve ficar próximo de 1 g. Incline a cabeça separadamente em cada direção e registre os eixos que respondem. Configure `IMU_PITCH_AXIS`, `IMU_ROLL_AXIS` (0=X, 1=Y, 2=Z) e sinais somente após esse teste. O ângulo Z é integrado e deriva; escolher eixos não substitui uma calibração tridimensional da montagem.
7. Só habilite compensação de inclinação quando os eixos, sinais e posição de repouso estiverem validados. UWB permanece uma etapa separada: não houve mudança no ranging.

## Verificação de software

Compilar `firmware/scanner` no ambiente `esp32s3`; executar os testes `spatial_math_test.cpp` e `lidar_packet_parser_test.cpp`; abrir/compilar o projeto no Unity 6000.5.4f1. Os testes matemáticos cobrem os quatro quadrantes da montagem medida, plano deslocado, inversão de sinal, pan horário/anti-horário e preservação dos valores no CSV. A validação no equipamento continua pendente.

Resultado desta implementação: firmware `esp32s3` compilado com sucesso (RAM 179636/327680 bytes; flash 853145/3342336 bytes). Testes de geometria/histórico/CSV e parser LiDAR aprovados. Unity 6000.5.4f1 encerrou a compilação em batch com código 0, sem erros C#; há avisos de API obsoleta já utilizada pelo projeto. Referências aos scripts/prefab legados foram removidas das quatro cenas afetadas. Não houve gravação no ESP32, geração de novo APK nem validação física/visual no Android nesta etapa.

## Correção posterior: controles ausentes

A remoção do `ViewerReceiver` revelou uma dependência que a compilação isolada não detectou: seu `Awake` criava o `ArScannerController`, responsável também pelo HUD. A cena não continha um controlador próprio e, portanto, abria sem os controles.

`CenaViewer` agora contém um `ArScannerManager` ativo, com os módulos e as referências do HUD salvos diretamente na cena. Permanecem iniciar/parar scan, velocidades, teste 2D, modo 180°/360°, marcação de posição, Pitch/Yaw, PLY, limpeza e logout. A inicialização não depende mais do receptor UDP removido.

`ViewerSceneValidation` verifica a presença e as referências desses módulos ao processar a cena para um build; um painel ausente ou desabilitado interrompe a geração do app. Seu teste `CheckViewerInPlayMode` abre a cena real, executa a inicialização e verifica os módulos ativos e a ausência de exceções. Esse teste passou no Unity; teste visual e operação com sensores no Android continuam dependendo do aparelho.

APK desta correção gerado com sucesso: `Builds/ScannerAR-interface-corrigida.apk` (36.151.897 bytes). O pacote foi verificado; ainda não foi instalado ou testado no aparelho por esta execução.

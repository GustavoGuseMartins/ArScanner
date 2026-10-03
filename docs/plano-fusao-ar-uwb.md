# Fusão AR + UWB para scanner parado

## Por que mudar o estimador

A PCB tem antenas a 100/100/175 mm. Uma única trinca de alcances frontal amplifica 5 cm de erro de cada rádio em aproximadamente 2,66 m de incerteza 3D quando a tag está a 2 m; o app só aceita até 0,5 m. Os CSV de 26/09 confirmam muitas trincas incompatíveis. Reajustar dois pontos não muda essa amplificação.

## Divisão de trabalho

| Dispositivo | Responsabilidade |
|---|---|
| ESP32 do visualizador | Fazer DS-TWR para os três módulos, preservar alcance bruto, resultado e instante de **cada** troca; futuramente expor qualidade de recepção para identificar reflexões. Não atribuir coordenadas AR. |
| ESP32-S3 do scanner | Projetar LiDAR no referencial mecânico, associar temperatura e informar pan/estado; manter a tag parada durante a localização inicial sempre que possível. |
| Celular | Usar pose AR da câmera e montagem da PCB para transformar cada âncora em posição no mundo no instante da medição. Acumular leituras de poses do telefone separadas espacialmente, estimar a tag com perdas robustas, avaliar residual e condicionamento, resolver lado da placa pela trajetória/visão e converter tag em eixo do pan. |

## Fluxo para scanner imóvel

1. Validar um perfil de alcance num terceiro ponto físico independente. Guardar bruto e corrigido.
2. Com scanner parado, mover o telefone lentamente por vários pontos de vista e **pausar** em cada um. A pausa limita o erro dos três alcances, obtidos em sequência; a mudança de posição entre pausas cria uma linha de base AR de dezenas de centímetros ou mais.
3. Agrupar medições de cada pausa, rejeitar ciclos grosseiramente incompatíveis e usar uma mediana por âncora e posição AR. Resolver por mínimos quadrados robustos. Quando a dispersão ainda não for suficiente para uma pose precisa, exibir uma pose aproximada se o resíduo permanecer limitado e mostrar a incerteza no painel; a pose precisa continua exigindo condicionamento melhor.
4. Converter a tag em eixo pela geometria medida da montagem, exibir que a origem é experimental e manter a pose fixa enquanto o scanner permanece no apoio. O yaw da cabeça não vem de uma única tag UWB e continua exigindo alinhamento ou outro sensor.
5. Aplicar suavização temporal apenas **depois** de aceitar uma posição. Se a solução perder qualidade, preservar a última posição validada somente para scanner declarado parado e informar que a referência está congelada; não recolocar a nuvem com uma leitura inválida.

O estimador multivista ignora novos ciclos enquanto o controle ou o status do scanner indicar varredura ativa ou retorno do pan. Ao começar o giro, ele descarta as amostras da posição antiga da tag e congela o eixo já aceito. Depois que o pan parar, uma nova coleta usa a posição atual da tag. O pan precisa estar parado durante cada coleta, pois a tag está na cabeça giratória.

## Sincronização e qualidade

O firmware do visualizador já mede as três antenas sequencialmente e guarda seus instantes internamente, mas ainda não os transmite. A versão de diagnóstico do APK registra pose AR por mensagem USB a partir de 26/09; isso permite testar a estratégia offline, porém a pose ainda corresponde ao recebimento do conjunto. Antes de rastrear um scanner em movimento, transmitir os três instantes, alinhar os relógios do ESP/celular e interpolar a pose da câmera. Como a tag gira com o pan, a transformação da tag até o eixo também deve usar o ângulo e o instante correspondentes.

O filtro de Kalman escalar existente no ESP só atua após uma trilateração instantânea aceita; ele não corrige alcance com viés nem cria geometria. Um filtro de estado para a pose só será ajustado após medir ruído em montagem estática, testar rejeição de reflexões e validar o posicionamento em pontos físicos conhecidos.

## Estado em 27/09

- CSV de alcances e calibração: disponível.
- CSV com pose AR da câmera: APK `Builds/ScannerAR-ar-trajetoria-20260926.apk` instalado; aguarda primeiro ensaio com posições distintas do telefone.
- Ajuste multivista offline para avaliar esse CSV: `tools/fit_multiview_uwb.py`, verificado com uma trajetória sintética conhecida.
- Estimador multivista no aplicativo: implementado em `Assets/Scripts/Spatial/UwbArMultiviewEstimator.cs` e conectado ao `UwbAnchorManager`. A revisão que congela o eixo durante o giro compilou e foi instalada no Samsung em 27/09: `Builds/ScannerAR-fusao-ar-uwb-20260927-v2.apk`.
- Ensaio físico desta revisão: pendente. Os CSV presentes no telefone terminam em 26/09 às 16:33 e ainda não contêm a trajetória AR da nova execução.
- Instantes individuais dos rádios no protocolo: ainda pendentes. O APK registra a pose AR no recebimento do conjunto, o que é suficiente para o ensaio estacionário com pausas, mas não para movimento rápido.
- APK de 27/09 com fallback AR + UWB aproximado: `Builds/ScannerAR-ar-uwb-aprox-20260927.apk`, instalado no Samsung. A tolerância de inconsistência grosseira passou para 0,25 m; leituras ainda incompatíveis continuam recusadas. O eixo aproximado é congelado quando o pan começa a girar e é rotulado com erro/incerteza.

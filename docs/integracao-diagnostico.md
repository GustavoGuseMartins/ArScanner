# Integração atual: scanner, base UWB e aplicativo

Atualizado em 26/09/2026. O histórico de ensaios e hipóteses anteriores está em [integracao-historico.md](integracao-historico.md); as instruções abaixo descrevem o código atual.

Implantação de 26/09/2026: `Builds/ScannerAR-uwb-auto-termica90.apk` instalado no Samsung SM-S916B; firmware da base gravado na COM5 e firmware do scanner gravado na COM6, ambos com hash verificado. A base, isolada do scanner, publicou 45 diagnósticos em cinco segundos com `radioMask=7`; a etapa 3 e as distâncias ausentes são esperadas sem a tag ligada. A comunicação simultânea base→celular→scanner e a precisão espacial ainda aguardam ensaio físico com os três alimentados.

## Arquitetura

| Ligação | Transporte e função |
| --- | --- |
| Scanner → celular | Wi-Fi `ArScanner_Net`, TCP `192.168.4.1:8888` para pontos e comandos, HTTP `:8889` para estado e imagem térmica |
| Base com três DWM1000 → celular | USB-C, CP210x a 115200 baud, diagnóstico JSON a cada ~100 ms com as três distâncias brutas |
| Base ↔ tag no scanner | DS-TWR UWB sequencial; o scanner é a referência física, e as três antenas viajam com o celular |

A base não depende mais de Wi-Fi no firmware normal. O aplicativo só habilita **INICIAR** após confirmar o scanner Wi-Fi, a base USB, seus três rádios e três distâncias recentes da tag. A cena AR aproveita cada conjunto de distâncias, mesmo quando o solucionador antigo da base sinaliza geometria incompatível. O celular fornece a pose da placa por ARCore. Sem posição aceita, novos pontos são suspensos e o eixo não aparece artificialmente na origem `(0,0,0)`.

## Posição e orientação

A correção de alcance é feita individualmente por rádio, com escala e deslocamento. Existe um perfil **provisório**, extraído dos ensaios anteriores a 0,8 m e 1,5 m; como a orientação da placa nesses ensaios não foi controlada, use a seção **CALIBRAÇÃO UWB** na tela inicial para obter um perfil próprio. Com a placa centralizada e voltada para a tag, informe duas distâncias físicas separadas por pelo menos 30 cm e capture 20 leituras em cada posição. O perfil é salvo no celular e não precisa ser repetido a cada abertura.

Três distâncias da pequena PCB (154 × 35,8 mm) geram duas soluções espelhadas e alta sensibilidade a erros. A escolha inicial usa a direção para a qual a câmera aponta; depois usa continuidade. O visor mostra erro, incerteza geométrica, qualidade, distâncias e atualizações aceitas/rejeitadas. Isto é uma **estimativa experimental 3D**, inclusive para deslocamento vertical, e ainda precisa ser comparada com deslocamentos reais medidos. No teste matemático, o GDOP é aproximadamente 70,9 a 2 m; com 10 cm de erro por distância, a incerteza pode chegar à ordem de metros. USB-C reduz problemas de transporte, mas não altera essa geometria.

Uma única tag não fornece yaw. Por isso, **Alinhar direção do scanner** continua na tela principal: com motores parados, aponte para o sentido do eixo do pan até o LiDAR. A posição manual e a altura sobre o apoio ficaram nos ajustes avançados como reserva, para quando não houver pose UWB. A tag do scanner foi medida 120 mm acima e 20 mm à frente do eixo e gira com a cabeça; o aplicativo remove esse deslocamento, considerando o ângulo atual do pan, antes de posicionar o eixo e a nuvem. A altura nominal do eixo do pan é 100 mm sobre o apoio; os 50 mm eixo→LiDAR já são aplicados na geometria do scanner. O pan não tem sensor de homing; o zero é relativo à partida e pode derivar se houver perda de passos.

## Imagem térmica e nuvem

A MLX90640 compartilha I²C com a MPU e requer a alimentação normal da PCB. Sua prévia nativa 32×24 foi girada mais 90° no sentido horário, ficando 24×32. A fusão usa a mesma orientação, campo efetivo de 75° horizontal × 110° vertical e a posição informada da lente: 50 mm à direita e 25 mm abaixo do LiDAR. Apenas pontos LiDAR projetados no campo térmico com quadro recente recebem temperatura; os demais permanecem neutros. A paleta usa °C absolutos para prévia e pontos. A correspondência fina precisa de teste com alvo quente conhecido em diferentes regiões da imagem.

O LOD opcional une pontos próximos no mesmo plano em polígonos, preservando bordas, vãos e descontinuidades térmicas. Opacidade, exportação e controles de scan permanecem disponíveis. RGB segue desativada no perfil normal porque GPIO4/5 ainda são usados pela tag UWB.

## Roteiro de verificação no equipamento

1. Ligue o scanner com alimentação normal da PCB e conecte o celular ao `ArScanner_Net`. Conecte a base ao celular por USB-C e autorize o acesso Android. A tela inicial deve mostrar scanner Wi-Fi, três rádios USB e, com a tag ligada, três distâncias.
2. Selecione **CALIBRAÇÃO UWB** e faça as duas capturas uma vez. Entre pelo botão **INICIAR**. Observe se a posição e o eixo aparecem somente após o rastreamento AR e três alcances recentes.
3. Meça uma trajetória horizontal e outra vertical. Compare a posição exibida e os contadores de leituras aceitas/rejeitadas. Se houver inversão ou deslocamento fixo, registre a orientação da placa e a posição física da tag antes de alterar offsets.
4. Com motores parados, alinhe yaw pela direção eixo→LiDAR. Inicie, pause e retome o scan. Confira chão/teto, opacidade e união LOD.
5. Abra a prévia térmica e use um objeto quente conhecido. Confira rotação, temperatura em °C e se os pontos coincidentes recebem a mesma cor.

Os firmwares-alvo são `firmware/scanner` ambiente `esp32s3` e `firmware/viewer` ambiente `esp32dev`. A gravação exige identificar as portas USB reais; COM3/COM4 Bluetooth não são as placas. Não use o perfil RGB de teste com os fios UWB nos GPIO4/5.

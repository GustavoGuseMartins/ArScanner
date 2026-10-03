# Análise do ensaio AR + UWB de 28/09/2026

Fontes: `diagnostics/20260928/latest/Uwb_20260928_233850_360.csv` (2.570 ciclos, 23:38:50–23:43:56 UTC) e `Screen_Recording_20260928_204353_ScannerAR.mp4` da mesma sessão.

## O que aconteceu

- O app exibiu posições UWB aproximadas desde 23:38:50, com incerteza geométrica frequentemente entre 1 e 3 m. O usuário movimentou o celular e reuniu amostras de diferentes posições. O apoio AR foi observado no vídeo em aproximadamente 0,84 m.
- O yaw ficou marcado como alinhado às 23:40:50, por toque manual no vídeo. O primeiro scan começou às 23:41:01 e terminou às 23:41:30. A primeira solução multivista com altura do apoio só foi aceita às 23:42:11. Portanto, o botão liberava captura com posição ainda aproximada.
- O segundo scan ocorreu entre 23:42:37 e 23:43:09. O vídeo comprova que o firmware recebeu os comandos e produziu pontos; a dificuldade para iniciar foi intermitente e relacionada aos pré-requisitos de pose/direção, não a uma falha permanente no comando de scan.
- O mesmo scanner parado recebeu yaw manual de aproximadamente 354,9°, 310,7° e 177,8° em enquadramentos diferentes. O botão fazia raycast do centro da câmera até o plano da mesa, mesmo quando o equipamento ou outro objeto cobria esse pixel. Assim, transformava um ponto atrás do objeto em suposta direção frontal.
- O resíduo multivista de aproximadamente 0,08 m e a dispersão calculada de aproximadamente 0,02 m medem apenas a consistência interna dos alcances com aquele modelo. Não incluem viés sistemático de cada rádio, erro na montagem nem uma medida independente do centro do scanner. Um filtro temporal reduziria tremor, mas não corrigiria esse deslocamento absoluto.

## Correções da próxima versão de teste

1. A captura automática passa a exigir solução multivista, altura do apoio observada e yaw alinhado. A pose aproximada continua visível para orientar a caminhada, mas não libera pontos.
2. O yaw manual e o automático recusam o centro da imagem quando a profundidade detecta um objeto à frente do plano da mesa. O alvo precisa ser um ponto livre da mesa, entre 25 cm e 1,5 m adiante do eixo. A interface explica o motivo da recusa e permite rearmar o automático depois de um yaw manual.
3. Após o primeiro ponto capturado, a origem multivista permanece congelada até a nuvem ser limpa e o scanner ser localizado novamente. Isso evita que o scan seguinte use outro eixo enquanto pontos antigos permanecem no mundo AR.
4. O CSV passa a registrar pose estimada, tag, alvo e fonte do yaw, altura/primeiro plano observado e distância horizontal entre esse primeiro plano e a tag. A próxima sessão permitirá distinguir um viés de alcance de um alvo visual escolhido incorretamente.

Ainda não há reconhecimento da frente física do LiDAR na imagem. O yaw automático continua sendo um ensaio guiado: exige enquadrar a mesa livre no sentido frontal. Antes de ajustar viés UWB ou escolher um filtro de posição, é preciso comparar os novos campos do CSV com a posição física do eixo medida na bancada em pelo menos duas distâncias.

# Ensaio no celular: posicionamento e yaw automáticos

Esta versão é um **protótipo de bancada** para scanner parado. A cena inicia em modo UWB automático se o celular já tiver um perfil de alcance salvo. A altura usa o plano AR somente quando um hit de profundidade enxerga o corpo do scanner acima desse plano. O yaw reaproveita o cálculo do botão existente e é acionado após um segundo de enquadramento estável. Ainda não há reconhecimento visual livre da frente do scanner; o usuário precisa apontar a câmera para o apoio **adiante do LiDAR**, como fazia antes de apertar o botão.

O APK refinado `Builds/ScannerAR-ensaio-refinado-20260928.apk` foi compilado e instalado com sucesso no Samsung SM-S916B. As validações de pose, multivista, cena e Play Mode passaram no editor, e o processo do app iniciou sem exceção Android observada. A precisão física do alinhamento, a ocultação dos pontos e a orientação térmica ainda precisam deste ensaio com o scanner conectado ao celular. Veja a análise da sessão anterior em `docs/analise-ensaio-2026-09-28.md`.

## Sequência

1. Com o scanner imóvel sobre uma mesa, deixe o pan parado. Ligue scanner e base UWB, conecte o celular à rede do scanner e a base à USB-C do celular. Abra o modo hardware. Um perfil UWB salvo dispensa refazer as duas distâncias nesta sessão.
2. Aponte o centro da câmera para o corpo do scanner sobre a mesa e aguarde a mensagem **Apoio AR observado**. O app exige que a profundidade mostre um objeto acima do plano, para evitar usar o piso atrás do scanner como altura da mesa.
3. Mova o celular lateralmente, fazendo pausas curtas em dois ou mais pontos, sem mover o scanner. Observe o estado UWB. A pose com altura do apoio só é aceita após alcances coerentes, separação suficiente entre pontos de vista e resíduo/condicionamento adequados.
4. Aguarde a mensagem de pose multivista aceita; a posição apenas **aproximada** não libera scan. Compare a posição do eixo com o scanner real e anote a diferença mostrada entre UWB e o objeto visto pela profundidade.
5. Aponte o **centro** da câmera para um ponto livre da mesa entre **25 cm e 1,5 m adiante do eixo na direção do LiDAR**, sem cobrir esse ponto com o equipamento. Mantenha o celular imóvel por cerca de um segundo. O yaw deve alinhar sozinho; confirme que o eixo indicado e a frente da cabeça coincidem. Se fizer uma correção manual, o botão **Reavaliar direção automaticamente** permite repetir o teste do automático depois.
6. Inicie uma varredura parada. Observe o eixo em posições de pan diferentes. A parede vista pelo verso deve desaparecer; superfícies próximas devem cobrir as camadas atrás. O menu avançado pode desligar o yaw automático para comparar.
7. Para a térmica, coloque uma fonte quente pequena no centro e nos quatro quadrantes da imagem, primeiro com pan parado e depois após um giro físico conhecido. Compare a prévia térmica e a cor nos pontos da mesma superfície. Não troque sinais ou offsets somente com base na aparência de uma vista traseira.

## O que registrar

- Vídeo ou capturas da tela no momento de **Apoio AR observado**, pose aceita e yaw alinhado.
- CSV de `UwbDiagnostics`, incluindo posição da câmera, âncoras, razão de rejeição e estado de captura.
- Distâncias físicas aproximadas: altura do eixo acima da mesa, scanner–celular e deslocamento lateral do celular.
- Para a térmica: posição física da fonte quente, quadrante na prévia, lado da superfície colorida na nuvem e ângulo do pan.

Se a frente do scanner não estiver enquadrada de maneira conhecida, o protótipo não tem informação suficiente para escolher yaw sozinho. Nessa situação, use o botão manual e registre o enquadramento em que o automático falhou; o próximo passo é reconhecer a frente do equipamento na imagem RGB/profundidade.

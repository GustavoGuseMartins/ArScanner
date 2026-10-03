# Deslocamento horizontal observado no ensaio de 28/09/2026

Fontes: as três capturas `Screenshot_20260928_221705_ScannerAR.jpg`,
`Screenshot_20260928_222208_ScannerAR.jpg` e
`Screenshot_20260928_222258_ScannerAR.jpg`, mais o CSV
`Uwb_20260929_011645_042.csv` com 8.620 linhas (22:16:45–22:33:58 no celular).
Os arquivos foram copiados para `diagnostics/20260928/latest-v2/`.

## Diagnóstico

- O yaw está coerente com o relato do usuário e não foi alterado. O eixo desenhado
  continua deslocado do scanner real nas capturas, inclusive após a aceitação de
  uma pose multivista. Às 22:17:05 a diferença horizontal entre a tag calculada
  e o ponto de profundidade guardado era 0,38 m. Às 22:22:08 era 0,68 m.
- A comparação de 0,68 m **não mede diretamente o erro do eixo**. O código
  interrompia os raycasts de profundidade assim que a altura e a pose multivista
  eram aceitas. O ponto de primeiro plano ficava armazenado enquanto o UWB
  continuava a mudar a pose. A captura das 22:22:58 olha para outra direção e
  não deve ser usada como referência visual do centro físico do scanner.
- O resíduo UWB na captura de 22:17:05 era 0,087 m e a dispersão interna era
  0,017 m. Esses números mostram a concordância das distâncias entre si, mas
  não incluem atrasos de antena dependentes do ângulo, reflexões nem erro dos
  offsets físicos. Repetir o mesmo tipo de leitura reduz ruído, mas preserva o
  deslocamento sistemático.
- O offset horizontal configurado entre tag e eixo tem 0,02 m; o offset entre
  base UWB e câmera tem 0,02 m na direção frontal. Mesmo com sinal invertido,
  sozinhos eles não explicariam um deslocamento de algumas dezenas de centímetros.

## Mudança para o próximo ensaio

1. O ponto de profundidade mostrado no painel passa a ser recente. Ao expirar,
   a comparação some; o app não apresenta mais um ponto antigo como medida atual.
2. Enquanto o usuário enquadra o corpo do scanner sobre o mesmo apoio, o app
   reúne pontos de profundidade de pausas em diferentes posições da câmera.
   Somente um grupo espacialmente coerente, com pelo menos três observações e
   25 cm de separação entre posições da câmera, pode influenciar a posição.
3. A profundidade só ajusta horizontalmente uma solução UWB multivista com
   altura do apoio. O ajuste por solução não ultrapassa 25 cm e conserva uma
   margem de 12 cm entre a superfície visível e a tag. A rotação não muda.
   Leituras visuais expiram após 60 s, e a nuvem já capturada mantém a origem
   congelada.
4. O CSV registra separadamente a tag UWB original, o alvo de profundidade,
   a correção aplicada, a quantidade de observações e a separação entre pontos
   de vista. O próximo teste deve comparar esses valores com o eixo físico.

Este ajuste é conservador porque AR Depth não identifica o scanner pelo nome:
outro objeto da mesma altura pode aparecer no centro. Se a observação visual não
for coerente, o app mantém a solução UWB. A precisão absoluta ainda precisa ser
medida na bancada. Uma calibração geométrica da base/câmera ou um marcador
visual reconhecível no scanner pode ser necessária se restar viés fixo.

## Validação e próximo teste

`ScannerPoseValidation` e a validação da cena em Play Mode passaram no editor.
O APK `Builds/ScannerAR-refino-visual-20260929.apk` foi gerado e instalado no
Samsung SM-S916B em 29/09; o processo abriu sem exceção Android observada no
início. A primeira compilação foi interrompida durante a etapa nativa depois de
uma longa pausa do computador; a repetição terminou com sucesso.

Para o ensaio físico: mantenha o scanner parado, enquadre seu corpo no centro da
câmera e faça pausas em pelo menos dois pontos separados lateralmente por 25 cm.
Confira se o painel informa um ajuste de profundidade; depois enquadre a mesa
livre à frente do LiDAR para o yaw e compare a origem desenhada com o eixo real.
Se houver novo deslocamento, guarde um print nessa posição: o CSV agora registra
se a correção visual ocorreu e qual era a tag UWB original.

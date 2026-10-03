# Pixels escuros na prévia térmica — 03/10/2026

Os 18 quadrados cinza isolados da foto enviada coincidem com os 18 pixels excluídos pela máscara de validade. Não apareceu um furo cinza adicional em uma posição marcada como válida. Essa conclusão identifica a origem dos furos; não certifica a precisão da temperatura nos 750 pixels restantes.

## Evidência e transformação de coordenadas

A foto `diagnostics/20261003/thermal-preview-investigation/Screenshot_20261003_153403_ScannerAR.jpg` mostra a mão na prévia e a indicação “Imagem parcial: 18 pixels sem calibração foram excluídos”. O pacote `thermal-stress-final.frame.bin` foi capturado depois, em outra cena, com faixa de 17,074 a 23,474 °C. Só a máscara fixa e a transformação de coordenadas foram comparadas entre essas capturas; as temperaturas não foram comparadas.

O perfil registrado é 1, sem espelho horizontal. Em `PointCloudTcpReceiver.ApplyCameraPreviewPayload`, o pixel nativo `(coluna, linha)` ocupa a posição `(23 − linha, coluna)` na imagem de 24 × 32, contando a linha da tela a partir do topo. O índice inferior da textura Unity é `(31 − coluna) × 24 + (23 − linha)`; esse armazenamento não muda a coordenada superior usada na foto. A máscara usa o mesmo índice nativo do byte térmico antes da rotação.

Os índices nativos excluídos são:

`5, 33, 61, 89, 117, 146, 174, 202, 231, 260, 288, 316, 344, 373, 401, 429, 458, 486`.

Exemplos na mão: o pixel 260 corresponde à coluna 15, linha 4 da tela; 231 a (16,7); 202 a (17,10); 174 a (18,14); 146 a (19,18); e 117 a (20,21). As posições cinza internas e as posições no fundo obedecem à mesma máscara.

## Comparação fotométrica

Os limites aproximados da prévia na foto original de 1080 × 2340 são x=382, y=108, largura=232 e altura=310. Foi calculada a mediana RGB de uma região de 3 × 3 pixels da foto em cada um dos 768 centros previstos.

Um critério aplicado somente à fotografia — diferença entre o maior e o menor canal RGB ≤20 e média RGB entre 60 e 110 — detectou exatamente 18 quadrados cinza. Seus índices são idênticos aos da máscara, com zero divergências. Nos pixels excluídos, a diferença entre canais ficou entre 6 e 14; nos 750 válidos, a menor diferença foi 71. Isso distingue os furos cinza de pixels válidos frios, que continuam coloridos em azul/ciano pela paleta.

![Comparação da foto com a máscara](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/thermal-preview-investigation/thermal-mask-photo-overlay.png)

## Limitações e implicação

A classificação acima vale para esta foto, seus limites e sua exposição. Ela não é um filtro de temperatura nem uma nova regra para aquisição. A cena posterior também não é um alvo uniforme controlado: a diferença média de aproximadamente 0,48 °C entre as duas paridades do padrão xadrez, nessa captura isolada, não basta para diagnosticar erro de calibração ou recomendar descarte de outros pixels.

Na prévia, os pixels inválidos são transparentes e deixam aparecer o fundo. Na fusão térmica da nuvem, a implementação recusa uma interpolação quando um pixel inválido teria peso positivo. Portanto esses furos não representam medições de frio nem temperaturas reais atribuídas aos pontos.

Não há evidência nesta foto para ampliar a máscara ou alterar a aquisição. Uma verificação futura da precisão nos pixels válidos exige imagens repetidas de um alvo estável com temperatura e geometria controladas. Os furos atuais podem ser explicados pela máscara existente, sem alterar a reconstrução que está funcionando.

Artefatos reproduzíveis: `thermal-mask-photo-analysis.json`, `thermal-mask-photo-overlay.png` e `analyze_thermal_mask_photo.py`, na pasta da investigação. Nenhuma fonte de produção foi modificada nesta auditoria; os originais da foto e do pacote foram preservados.

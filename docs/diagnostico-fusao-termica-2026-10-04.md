# GY-25, faixas sem cor e mão sobre o fundo — 04/10/2026

Foi analisada a gravação `Screen_Recording_20261004_113649_ScannerAR.mp4`, copiada do Samsung conectado, junto aos registros da mesma sessão. Os originais foram preservados no celular. Cópias, imagens extraídas, resumos e logs ficam em `diagnostics/20261004/thermal-fusion-audit/`.

## Resultado observado

| Questão | Evidência e conclusão |
| --- | --- |
| GY-25 está sendo usada? | Leitura ativa em 370 consultas: identidade 104, bias concluído, zero erros e idade máxima de 52 ms. Acompanhamento do giro e inclinação dos pontos ficaram desligados em todas elas, incluindo 206 durante captura. |
| A referência funcionou? | Concluiu às 11:31:56, com 3.007 ms de repouso. O acompanhamento não foi ativado. A referência invalidou às 11:32:14; não houve nova lacuna no contador de aquisição. A consulta periódica não registra a amostra transitória que provocou a invalidação. |
| A térmica manteve 8 quadros/s? | A primeira fase entregou 7,50 quadros completos/s por diferença do contador. Às 11:32:09 o alvo interno caiu de 8 para 4, coincidindo com descartes de leitura; a fase seguinte entregou 3,91 quadros/s. Não houve erro I²C nem timeout. |
| Quanto recebeu temperatura? | Os contadores avançaram 300.599 leituras LiDAR e 12.247 associações térmicas: 4,07%. Isso mede os retornos do firmware, não a porcentagem da nuvem voxelizada ou dos pixels da câmera. |
| A nuvem continuou atualizando? | Às 11:36:05 a orientação AR da câmera saltou 88,24° entre duas amostras separadas por 66,7 ms. A direção manual foi invalidada e novos pontos foram recusados. O scanner continuou capturando por cerca de 35 segundos; os frames de 25 e 38 s mostram os mesmos 8.358 pontos. |

Horários acima em America/Sao_Paulo. Resumos: `gy25-session-summary.json`, `thermal-session-summary.json` e `uwb-heading-session-summary.json`. O JSONL original possui SHA-256 `6256777c3e19e346283bb65678231ae117d4f77f2bfa582b1382e6acc288edff`.

## Por que aparecem faixas brancas

No renderer, a geometria sem temperatura recebe cinza claro, percebido como branco. A flag `0x04` indica indisponibilidade térmica; o valor de transporte 21 °C não é apresentado como medição. Uma temperatura válida acima de 45 °C também pode chegar ao branco da paleta, mas os extremos mostrados nesta gravação ficaram em 14,7–33,7 °C.

O LiDAR mede uma fatia vertical de 360°, enquanto a lente térmica cobre apenas sua frente. Na configuração atual, o perfil 1 observa +X da cabeça; a metade oposta da fatia não tem imagem correspondente. O campo nominal configurado, após a rotação da imagem, é 75° horizontal × 110° vertical. A [Melexis documenta a matriz 32 × 24 e as opções de campo óptico](https://www.melexis.com/en/product/mlx90640/far-infrared-thermal-sensor-array); isso não confirma sozinho a calibração óptica da montagem física.

Além dessa cobertura física, a associação exige diferença de até 200 ms entre o ponto e o quadro. Durante a fase de 4 quadros/s, 146 de 204 consultas em captura tinham idade térmica acima de 200 ms; a mediana foi 255,5 ms. Isso sustenta a hipótese de alternância temporal entre fatias com e sem temperatura. A idade consultada por HTTP não é a diferença temporal de cada ponto, portanto 71,6% das consultas não significa 71,6% de descarte de pontos.

Os 18 pixels de calibração excluídos também recusam suas vizinhanças de interpolação. O app e o LOD não preenchem regiões sem temperatura com cores de regiões vizinhas. A captura não registra motivos individuais de recusa; não permite repartir os 95,93% sem associação entre campo óptico, idade, máscara, distância e pose.

## Por que a mão pode colorir a parede

A câmera térmica informa temperatura em uma direção, sem medir profundidade. A lente e o LiDAR têm centros diferentes, com offset configurado de 25 mm vertical e 50 mm horizontal. Uma mão muito próxima pode ocupar o pixel térmico enquanto o feixe LiDAR, em outra trajetória, ainda alcança a parede. A projeção atual pode então associar a leitura da mão ao ponto do fundo. O corte de 40 cm usa a distância retornada pelo LiDAR: ele não detecta uma mão que o feixe não atingiu.

Foi encontrado também um erro de software independente: a projeção usava a pose atual da cabeça para consultar um quadro anterior. Durante o giro, isso desloca lateralmente a temperatura. No pan nominal de 2 RPM, uma diferença de 200 ms corresponde a 2,4°. O quadro completo reúne duas subpáginas; na fase de 4 quadros/s elas ficaram separadas, em média, por 126,8 ms. Compensar a pose do ponto médio não elimina movimento ocorrido entre essas duas exposições.

A nuvem é acumulada: geometria antiga da parede permanece quando um objeto próximo aparece. Depois da perda de direção, a nuvem congelada também não serve para avaliar novas associações. A gravação confirma pontos de cores diferentes sobre a geometria, mas não oferece distâncias e imagens térmicas sincronizadas suficientes para demonstrar a causa exata do episódio da mão.

## Correções implementadas

- A projeção térmica consulta o ângulo no instante do LiDAR e no instante do quadro, transforma o ponto completo para a cabeça daquele quadro e preserva o offset das lentes. Uma troca de quadro durante a consulta recusa a associação.
- Com GY-25 ativa, um histórico de 64 leituras fornece yaw no instante correto. Referência, modo ou validade diferentes descartam o histórico; não há retorno silencioso ao ângulo do motor. A geometria do ponto também passa a usar esse histórico.
- A validade do estado `ready` usa comparação textual, eliminando a dependência de endereços de literais C++.
- O HUD distingue leitura da GY-25, origem do giro dos pontos e inclinação. Exibe taxa térmica medida, redução automática e aviso quando o scanner varre mas o app recusa novos pontos.
- A referência explícita da GY-25 mede também o resíduo do giroscópio nos três eixos, ponderado pelo intervalo real de aquisição. O estimador de yaw desconta esse resíduo; as leituras brutas e o bias de inicialização permanecem preservados. Falhas, lacunas ou nova referência zeram juntos o tempo e a integral da janela.
- O intervalo de 10 ms entre tentativas térmicas conta do início da tentativa. A aquisição longa deixa de acrescentar esse intervalo ao final; a pausa normal da tarefa e a serialização I²C continuam ativas.

O limite de 200 ms, a proteção de redução de taxa, a máscara térmica e os critérios de perda do referencial AR foram preservados. Esta revisão não implementa profundidade térmica, remoção de oclusão ou confirmação dos extrínsecos físicos. A sustentação de 8 quadros/s durante varredura com LiDAR e pan permanece pendente; o ensaio abaixo confirma a taxa com prévia térmica e acompanhamento GY-25 ativos, com os motores parados.

## Validação e próximo ensaio

O teste nativo de aquisição térmica passou, incluindo pose de dois instantes, cabeça parada, mudança de ângulo, histórico ausente, valores inválidos, limite temporal e publicação concorrente. O teste de histórico yaw e comparação textual passou, incluindo interpolação, wrap do relógio, expiração, duplicatas e reset. O teste matemático espacial compilou; sua execução específica foi bloqueada pelo Controle de Aplicativo do Windows, sem contorno.

O primeiro firmware desta revisão `esp32s3` compilou com RAM 194.852 bytes e flash 880.925 bytes. A revisão final com compensação do resíduo GY-25 e intervalo térmico compilou com RAM 194.900 bytes e flash 881.325 bytes. Os testes Unity de pose, LOD, controles, máscara térmica, qualidade LiDAR e temporização passaram. O teste do driver IMU também executou com sucesso, incluindo repouso com resíduo nos três eixos, giro real, mudança de gravidade, intervalos irregulares, duplicatas e reset após falha/lacuna. Artefatos e hashes ficam em `validation.json`, no diretório de evidências.

Artefatos entregues: `Builds/ScannerESP32S3-gyro-reference-bias-20261004.bin` (imagem da aplicação, 881.696 bytes; SHA-256 `ba28d9831f3b2b5ba707708af91943c83cadc2877c90be45c96f5f2129df4978`) e `Builds/ScannerAR-thermal-fusion-audit-20261004.apk` (36.527.181 bytes; SHA-256 `9077a381f81a30d9c98b7f09d8bc6f1683d674fa9d2a306a13e2873314e001fa`). Ambos foram instalados e verificados no ESP32-S3 conectado e no Samsung. O APK foi atualizado preservando os dados do aplicativo; sua assinatura e o hash instalado foram conferidos. O firmware da base UWB não foi alterado. A imagem anterior `ScannerESP32S3-thermal-fusion-audit-20261004.bin` foi preservada como evidência da primeira medição, que motivou a correção do resíduo.

## Ensaio no equipamento conectado

O scanner ficou apoiado e imóvel, conforme confirmado pelo usuário. Nenhum comando de motor ou captura foi enviado durante estes ensaios. As duas movimentações abaixo foram feitas pelo usuário no conjunto inteiro.

| Ensaio | Resultado |
| --- | --- |
| Referência antes da correção do resíduo | Concluiu em cerca de 3 s e permaneceu válida, mas yaw variou −10,272° em 39,690 s parado. Zero erros de leitura e zero lacunas de orientação. |
| Referência após a correção | Concluiu com 3.006 ms de repouso. Yaw variou −0,420° em 59,115 s parado. Referência válida, idade máxima 18 ms, zero erros de leitura e zero lacunas de orientação. |
| Referência final, após retorno e com prévia aberta | Concluiu com 3.002 ms de repouso. Na geração 2, yaw variou −0,143° em 38,955 s parado. Referência válida, idade máxima 17 ms, zero erros/lacunas. Foram publicados 304 quadros térmicos completos (7,804/s), sem novos descartes, erros ou timeouts. |
| Giro manual à direita | Yaw aumentou cerca de 74°; pan mecânico ficou em −152,925°. A referência continuou válida. A estimativa visual de um quarto de volta não constitui referência angular para medir precisão ou escala. |
| Retorno manual à esquerda | O yaw diminuiu, confirmando o sentido reverso. O retorno já havia ocorrido na primeira amostra da etapa seguinte; não foi registrada a trajetória completa do retorno. Não se deduz erro angular pelo fechamento manual. |
| Taxa térmica após o ajuste de intervalo | Já estava com alvo 8 e medição 7,801 quadros/s antes da nova referência e antes de qualquer pedido de reativação. Houve dois descartes no início desta inicialização, sem redução para 4. Nos 59,115 s seguintes, foram publicados 461 quadros completos (7,798/s), sem novos descartes, erros I²C ou timeouts. |
| Prévia HTTP antes do ajuste de intervalo | 80 de 80 quadros recebidos, todos com 872 bytes e máscara coerente de 18 pixels excluídos. Zero falhas de conexão ou reinicializações. |
| Aplicativo atualizado | Menu, visor AR, painel à direita e sliders de opacidade/velocidade conferidos no celular. Prévia térmica aberta no próprio app, com aproximadamente 7,8 quadros/s e aviso dos 18 pixels excluídos. |

Registros: `connected-orientation-retry.jsonl`, `connected-gyro-bias-retry.jsonl`, `connected-gyro-right-turn.jsonl`, `connected-gyro-return-preview.jsonl`, `connected-gyro-warm-final.jsonl`, `connected-tests-summary.json`, `connected-preview-test.summary.json` e imagens `phone-live-ar-hud.png` / `phone-live-thermal-preview.png`. A etapa final é resumida somente após a nova referência e ativação da geração 2; a consulta inicial ainda refletia a geração 1 e não integra essa medição. Os logs de gravação preservam as verificações de hash do ESP32-S3. O aquecimento entre as medições não foi controlado: a comparação mostra o comportamento observado, sem atribuir toda a diferença a uma única causa.

O acompanhamento de yaw permanece relativo à direção definida inicialmente pelo usuário. Pitch/roll continuam sendo diagnósticos; não são aplicados à geometria neste modo. Ainda falta uma captura real para avaliar a precisão angular durante o giro do motor, a sustentação da taxa térmica nessa carga e a distribuição das cores/LOD na nova nuvem.

Para separar as causas no equipamento, usar primeiro pan parado e um alvo quente a 0,8–1,5 m que intercepte claramente o LiDAR. Conferir a prévia térmica e a mesma superfície na nuvem; repetir em dois ângulos de pan. Depois testar um anteparo próximo, verificando se o LiDAR realmente mede esse anteparo. Para usar GY-25, concluir a referência e ativar **Acompanhar giro com GY-25** antes da captura; referenciar sozinho não a aplica. Repetir a varredura observando a taxa medida e a validade da direção AR.

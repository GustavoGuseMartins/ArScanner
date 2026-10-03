# Análise das três tentativas de 30/09/2026

Meta informada pelo usuário: erro de posição do eixo de no máximo **10 cm** e erro de yaw de no máximo **10°**, usando o próprio scanner, **sem marca visual adicionada**. Este documento registra o diagnóstico do código e dos testes anteriores à correção. A implementação posterior está descrita em [implementação de posição e pan](implementacao-pose-pan-2026-09-30.md).

## Evidência examinada

Dados copiados do Samsung SM-S916B conectado, preservando os originais:

- `diagnostics/20260930/latest/Screen_Recording_20260930_194027_ScannerAR.mp4`, duração 6 min 31,60 s; quadros examinados na mesma pasta em `frames/`.
- CSVs UWB de hoje, em particular `Uwb_20260930_223405_933.csv`, `Uwb_20260930_223550_002.csv` e `Uwb_20260930_223637_248.csv`.
- Captura `Screenshot_20260930_193714_ScannerAR.jpg`.
- Código atual de pose, multivista, refino visual, yaw, recepção e renderização.

Horários abaixo são de São Paulo (UTC−3). Os nomes dos CSVs usam UTC. O tempo do vídeo é aproximado: o começo corresponde a cerca de 19:33:56. O vídeo permite comparar a projeção do eixo com o equipamento, mas não fornece uma medição independente em centímetros ou graus. Deslocamentos numéricos abaixo são mudanças entre soluções do aplicativo, não erro físico absoluto medido.

## As três tentativas

| Tentativa | Registro | Resultado |
| --- | --- | --- |
| Primeira filmada | 19:34:05–19:35:40 | Posição multivista aparece, mas yaw nunca fica alinhado; captura permanece bloqueada. |
| Segunda | 19:35:50–19:36:36 | Yaw automático de 297,66° às 19:36:20; captura liberada por parte da sessão, sem iniciar scan. |
| Terceira | 19:36:37–19:40:34 | Alternância de soluções e ajustes de yaw antes de iniciar scan às 19:37:39; depois a última origem é congelada. |

Na primeira, 481 das 800 linhas mostram o último motivo de yaw como “centro da câmera atingiu outro plano”; 99 mostram falta de alvo no apoio e 47 mostram alvo válido aguardando estabilidade. São estados registrados, não 800 tentativas independentes. A evidência é compatível com bloqueio pelos pré-requisitos da pose/direção; não comprova falha no envio do comando de iniciar.

## Causa comprovada dos saltos de posição

`UwbAnchorManager.cs:1240` tenta primeiro estimar a tag na altura do apoio. Se esse cálculo falha, aceita uma solução 3D livre. Essa solução substitui diretamente a posição anterior (`:1274`), sem comparar continuidade ou validar sua altura contra o apoio já confirmado.

| Horário | Mudança registrada na terceira tentativa |
| --- | --- |
| 19:37:07.393 | Primeira pose multivista: eixo Y=0,1161 m, compatível com apoio Y=0,0161 m + altura mecânica de 0,10 m. |
| 19:37:07.491 | Outra pose aceita: Y=−0,4433 m. Mudança total de **1,3343 m em aproximadamente 0,10 s**, com pose válida antes e depois. |
| 19:37:18.509 | Yaw alinhado e captura liberada com eixo Y=−0,4152 m: **53,13 cm abaixo da altura prevista pelo próprio apoio**. |
| 19:37:35.420 | Volta para Y=0,1161 m e recebe correção visual horizontal de 25 cm; mudança total de **67,27 cm**. |
| 19:37:39.451 | Começa scan com a origem final aproximadamente (−0,3385; 0,1161; 0,7215) m. |

O salto de 1,3343 m ocorre por volta de 03:12 no vídeo; a volta para a altura do apoio, por volta de 03:40. O vídeo mostra o indicador em posições diferentes do scanner, compatível com as transições registradas.

O critério de captura (`UwbAnchorManager.cs:187`) exige a existência de uma altura observada, mas não exige que **a pose atual respeite essa altura**. Assim, uma solução livre pode liberar captura apesar da incompatibilidade vertical. Essa é a primeira correção necessária.

O resíduo melhora ligeiramente no salto de 1,3343 m (11,53→11,10 cm), e a dispersão interna permanece perto de 2,5 cm. Isso mostra por que aceitar pela concordância entre alcances não basta para certificar posição física ou cumprir a meta de 10 cm.

## Por que depois fixa longe

Novas soluções aceitas substituem a origem diretamente. Sem nova solução aceita, o app conserva a anterior (`UwbAnchorManager.cs:1297`). Durante o giro, congela a origem (`:1142`); havendo pontos capturados, mantém esse congelamento (`:1165`). Preservar a origem após aquisição evita alterar a nuvem, mas hoje pode preservar uma localização inadequada escolhida antes do scan.

O refino por profundidade só roda junto de uma nova solução multivista com altura (`:1257`). Não é atualizado independentemente quando surgem observações visuais melhores. Nesta gravação houve **uma atualização positiva efetiva**, às 19:37:35.420, usando oito observações e cerca de 1 m de separação entre pontos de vista. As 1.486 linhas com correção visual repetem esse resultado, sobretudo durante o congelamento; não representam 1.486 refinamentos.

`UwbVisualPositionRefiner.cs:18` conserva uma margem de 12 cm até a superfície observada e aplica somente 65% do excesso, limitado a 25 cm. Ele verifica a região do corpo, mas não conhece o centro mecânico do eixo. Portanto, seus limites atuais não certificam uma precisão absoluta de 10 cm. A correção atingiu o limite de 25 cm na terceira tentativa. **Não ocorreu perda posterior dessa correção nesta gravação**; a expiração do refino é um risco do código, não a causa demonstrada deste ensaio.

## Yaw inicial e ajuste automático

A cena inicia `droneYawDeg=0` e o renderizador aplica a correção local de 200°. O indicador já mostra direção quando há posição, antes de `PreviewHeadingAligned` (`CenaViewer.unity:752`, `:793`; `ThermalPointCloudRenderer.cs:209`). A direção inicial visível ainda não foi observada no mundo AR. A correção local de 200° não deve ser apagada para tentar resolver isso: ela pertence à transformação da geometria.

O automático usa o ponto livre do apoio no centro da tela (`UwbAnchorManager.cs:795`) e calcula tag→alvo. Ele valida plano, distância e estabilidade, mas **não reconhece a frente física do scanner**. O vídeo mostra o enquadramento do corpo e do apoio mudando durante a localização. Um ponto aceitável do apoio não prova que está na direção eixo→LiDAR.

Na terceira tentativa, o yaw foi 150,33° pelo automático às 19:37:18; 164,11° pelo botão manual às 19:37:27; e 129,91° pelo automático rearmado às 19:37:38. A diferença entre o manual e o último automático é 34,20°. Esses valores não são erros angulares medidos contra uma referência física. A posição muda entre alinhamentos, alterando a direção tag→alvo. Além disso, depois do primeiro alinhamento, o automático deixa de reavaliar porque `PreviewHeadingAligned` já está verdadeiro (`:966`).

## Associação temporal: interpretar a telemetria corretamente

Muitas linhas mostram “sem histórico AR contínuo”. Isso **não significa que todas essas leituras foram descartadas**. O receptor entrega o pacote em `Update`; o estimador consulta o histórico; a pose AR atual só entra em `LateUpdate`, depois da gravação do CSV. A primeira consulta pode ainda não ter a amostra posterior necessária para interpolar `t3`. O código mantém o pacote para tentar novamente no próximo frame.

Evidência: 39 dos 40 aumentos no conjunto multivista da terceira tentativa aparecem em linhas com essa mensagem; na primeira, todos os 36 aumentos aparecem assim. O CSV pode misturar o motivo da primeira consulta de um pacote novo com a pose/contadores atualizados por um pacote anterior. É necessário registrar o identificador da leitura efetivamente consumida e o resultado do retry antes de concluir perda de dados ou rastreamento.

## Implementação proposta, em ordem

1. **Consistência de altura e liberação.** Representar explicitamente se a pose respeita o apoio. Após confirmar o apoio, impedir uma solução livre incompatível de substituir a pose ou liberar scan. Associar a liberação à pose atual, não a flags históricas independentes.
2. **Candidato e pose confirmada.** Exigir confirmação em observações distintas antes de comprometer uma nova localização. Registrar deslocamento proposto e fonte; mudanças grandes devem iniciar revalidação, invalidando o yaw derivado da origem anterior. Suavização visual só depois de validar o candidato.
3. **Refino visual independente.** Atualizar a evidência visual mesmo quando o conjunto UWB de uma pausa está cheio. Usar a geometria conhecida do scanner para relacionar superfícies observadas ao eixo, em vez de interpretar o pixel central como centro do equipamento. Preservar uma pose confirmada enquanto a nova evidência é insuficiente.
4. **Yaw sem marca adicionada.** Primeiro deixar explícita a direção ainda desconhecida e impedir alinhamento sobre pose incoerente. Depois prototipar reconhecimento do corpo/cabeça pela câmera do celular, aproveitando a geometria/modelo 3D existente e o pan para observar a direção eixo→LiDAR. Combinar essa observação com profundidade e UWB. O método atual pelo ponto do apoio permanece como alternativa guiada. Reconhecimento do objeto é trabalho adicional, não uma capacidade já fornecida pelo raycast.
5. **Registro e validação.** Registrar por decisão: pacote, fonte, altura usada, candidato, deslocamento, confirmação, motivo de bloqueio e versão da pose usada no yaw. Cobrir com regressões a troca altura→3D incompatível, atualização visual sem nova amostra UWB e invalidação do yaw após deslocar a origem. Repetir ensaio físico com eixo e direção medidos, comparando posições da câmera e pan; só o ensaio confirma ≤10 cm e ≤10°.

Para profundidade, usar múltiplas vistas continua relevante: a documentação primária do [ARCore Depth](https://developers.google.com/ar/develop/depth) explica que a profundidade é estimada a partir do movimento e que superfícies pouco texturizadas podem ser imprecisas. Ela fornece geometria de superfície, não a identidade ou a frente do scanner. Sem referência física medida, nem o resíduo UWB nem a dispersão interna podem servir como certificado das metas.

## Observação paralela de hardware

Os quadros mostram MPU ausente e térmica sem quadros, com erros −103/−100; em alguns instantes a consulta I2C também mostra ausência de ACK. Isso merece uma revisão separada da inicialização/comunicação dos sensores. Não foi usada como explicação dos saltos comprovados da pose ou como evidência de orientação absoluta.

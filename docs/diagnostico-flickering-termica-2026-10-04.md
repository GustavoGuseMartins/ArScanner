# Térmica a 4 quadros/s, torção e flickering — 04/10/2026

Os novos registros confirmam que a câmera térmica reduziu de 8 para 4 quadros/s ao iniciar a captura. Também foi identificado um comportamento do LOD que retirava superfícies inteiras enquanto chegavam novos pontos, favorecendo o flickering. A GY-25 foi lida e referenciada, mas seu yaw e sua inclinação permaneceram desligados na geometria de todas estas capturas; ela não determinou a torção observada.

Foram implementadas correções de prioridade da aquisição térmica, atualização regional do LOD, união de traços medidos com espaçamentos diferentes e visibilidade dos pontos. A causa da torção ainda não está demonstrada. O firmware novo foi gravado e conferido com os motores parados; manter 8 quadros/s durante varredura e conferir a nova nuvem no celular continuam pendentes.

## Evidências usadas

Foram preservados o vídeo `Screen_Recording_20261004_173027_ScannerAR.mp4`, os prints de 17:27:39 e 17:30:22 e os arquivos `Scanner_20261004_202232_376.jsonl` / `Scanner_20261004_202607_299.jsonl`, acompanhados pelos CSV UWB. Os nomes dos registros usam UTC; os horários neste relatório estão em **America/Sao_Paulo**, entre 17:22 e 17:30 de 04/10/2026. As cópias e seus hashes ficam em `diagnostics/20261004/scan-1730-investigation/`.

| Observação | Resultado dos registros |
| --- | --- |
| Cadência antes de capturar | Em 59,640 s de repouso, foram publicados 465 quadros completos: **7,797 quadros/s**. Alvo 8, sem aumento dos descartes nesse intervalo. |
| Queda para 4 | A primeira consulta com captura ativa foi às 17:23:32.173. Às **17:23:32.520**, o alvo já era 4 e os descartes de RAM passaram de 2 para 5. |
| Transporte térmico | Barramento manteve 400 kHz; **zero erros I²C, zero timeouts e zero quadros inválidos** nas consultas. A máscara permaneceu em 18 pixels. |
| Duração térmica | Em captura a 4 quadros/s: mediana **73 ms**, máximo 88 ms. Em repouso: mediana **53 ms**. Esse campo inclui cópia, verificação e cálculo. |
| Associação de temperatura | **9.481 associações para 231.533 retornos LiDAR válidos: 4,095%**. Não é a porcentagem dos voxels exibidos nem dos pixels bons da câmera. |
| GY-25 na geometria | **478 de 478 consultas com acompanhamento de yaw desligado**; inclinação também desligada, inclusive nas 215 consultas durante captura. |
| Descartes de pose | **39** no total, frente a aproximadamente 231 mil retornos válidos. Nenhum retorno foi marcado como sinal fraco nessas consultas. |
| Aceitação pelo visor | Os **1.708 registros UWB durante captura** indicavam aceitação de pontos e rastreamento AR. Nas três varreduras da segunda sessão, a origem e a direção estavam fixas nas consultas. |

Os registros periódicos não mostram cada tentativa de RAM, cada ponto recusado ou movimentos entre consultas. O usuário confirmou que o scanner ficou parado, sem trancos e sem cabo sendo puxado; não se atribui o problema a uma movimentação externa não relatada.

## Por que a térmica reduziu e as cores ficaram escassas

O firmware descarta uma leitura quando outra subpágina chega enquanto sua RAM ainda está sendo copiada. Após três ocorrências consecutivas, reduz de 16 para 8 subpáginas/s, equivalentes a 8 e 4 quadros completos/s. Isso preserva a coerência dos dados, mas a redução permanece após parar: não há recuperação automática para 8. Uma ocorrência adicional levou o contador a 6 às 17:23:33.621; não houve novas ocorrências nas capturas seguintes.

Antes da correção, aquisição térmica e GY-25 tinham prioridade 1 no mesmo núcleo da fusão dos pontos, com prioridade 2. A diferença observada entre 53 ms em repouso e 73 ms durante captura sustenta pressão de processamento como hipótese principal para perder a margem de 62,5 ms por subpágina. O evento inicial ocorreu antes de o contador LiDAR registrar retornos; a escrita do checkpoint de pan no início também pode introduzir uma pausa transitória. As consultas não permitem repartir a contribuição dessas etapas. O checkpoint foi preservado.

A associação térmica aceita diferença de até **200 ms** entre o ponto e a imagem. Durante captura a 4 quadros/s, **150 de 214 consultas** tinham idade térmica acima desse valor; a mediana foi **267,5 ms**. Isso sustenta a explicação das faixas sem cor, mas a idade consultada não é a diferença temporal de cada ponto: 150/214 não é uma taxa medida de descarte de pontos.

A cobertura óptica também é parcial: o LiDAR mede uma fatia de 360°, enquanto a lente térmica observa sua frente em 75° horizontal × 110° vertical na configuração atual. Os **18 pixels excluídos pela calibração EEPROM** e suas vizinhanças de interpolação continuam sem medição. A máscara e a EEPROM foram conservadas, sem preencher esses pixels com valores inventados. O visor já preserva uma temperatura real quando um retorno posterior sem temperatura chega ao mesmo voxel.

Não há, nestas consultas, evidência de falha de alimentação ou cabos que explique a queda de cadência. Isso não constitui uma medição elétrica do hardware nem elimina todos os possíveis efeitos físicos durante giro.

## Torção: o que está confirmado e o que falta

Estas capturas usaram o histórico do motor para projetar os pontos e sincronizar a térmica. As referências GY-25 feitas pelo usuário não ativaram seu acompanhamento. Pitch e roll também não foram aplicados. Portanto, uma integração de yaw da MPU não deformou estas nuvens.

Como diagnóstico independente, três referências da GY-25 perderam validade perto de pan −339° a −345°, embora os polls não mostrassem novos erros de leitura ou lacunas de aquisição. A amostra transitória responsável não estava registrada no firmware anterior. O diagnóstico novo conserva essa amostra e o motivo, permitindo distinguir gravidade fora da faixa, lacuna, leitura incompleta e gyro inválido sem relaxar os critérios de validade.

Uma estimativa separada encontrou um pequeno componente da aceleração relacionado ao pan, equivalente a aproximadamente **0,8–1,3°**, mas o ajuste explicou somente **26–38%** da variação. A comparação entre gyro médio e gravidade média variou de **1,25° a 4,58°** entre as voltas. Esses snapshots, aproximadamente uma vez por segundo, não demonstram uma inclinação física constante que possa ser calibrada. Não foi aplicada compensação de pitch/roll baseada nessa estimativa.

Na primeira sessão, a direção manual mudou entre varreduras; acumular pontos sem limpar após realinhamentos pode misturar referenciais. Na segunda sessão, a direção permaneceu constante, portanto essa possibilidade não explica toda a observação. Ainda falta separar geometria medida, montagem, escala/temporização do motor e efeitos de exibição. Este trabalho não declara a torção corrigida.

## Mudanças implementadas

**Aquisição térmica.** A tarefa de aquisição passou para prioridade **3**, acima da fusão em prioridade 2. Continua cedendo durante esperas I²C e na pausa normal da tarefa. O barramento mantém um único proprietário, e os pulsos do motor continuam no timer/interrupt de hardware. O tempo somente da cópia RAM agora aparece como `thermalRamReadDurationMs` no HTTP e `ram_ms` na serial, separado da duração total que inclui cálculo.

**Diagnóstico GY-25.** O firmware conserva motivo, contador, instante, intervalo, aceleração XYZ e norma da gravidade do último evento que invalidou uma referência. Uma leitura incompleta não apresenta uma amostra antiga como causadora. Os dados sobrevivem a nova referência, mudança de modo e reinicialização automática do driver; um reboot inicia novo contador. O HTTP e os registros do aplicativo recebem esses campos. Modo de orientação, critérios de validade e aplicação de pitch/roll foram preservados.

**LOD e flickering.** Retornos compatíveis passam a conservar as superfícies exibidas. Uma observação incompatível retira os retângulos afetados, permitindo conservar superfícies independentes enquanto a reconstrução seguinte é preparada. Resultados atrasados conferem a identidade dos pontos, inclusive quando uma posição do buffer circular foi reutilizada. Um hotspot pode retirar um retângulo grande inteiro; isso é uma limitação conservadora da divisão em retângulos, sem apagar outras superfícies independentes.

**Planos maiores.** O suporte passa a reconhecer traços repetidos cujo espaçamento é maior numa direção do que na outra. O caso de teste com **441 pontos em uma malha de 35 × 80 mm** produziu três planos e cobriu 434 pontos. Esse espaçamento é um exemplo de validação, não um preenchimento fixo de qualquer lacuna. Porta, amostra ausente, anel, linhas isoladas, diferença de profundidade e fronteira térmica continuam impedindo preencher regiões sem suporte.

**Visibilidade dos pontos.** Os quads individuais permanecem visíveis ao caminhar paralelamente à parede e pelos dois lados. Isso corrige perda de exibição de retornos existentes sem fabricar novos retornos. Os limites artificiais de **0,15–20 m** do visor foram removidos, atendendo ao pedido de manter leituras válidas sem corte por distância. Zero e coordenadas não finitas continuam recusados. O limite próximo usado apenas na associação térmica, para paralaxe, não elimina geometria LiDAR.

O alvo térmico continua 8 quadros/s. A redução de proteção, a janela de **200 ms**, a máscara dos **18 pixels** e a recuperação somente por pedido explícito continuam ativos. Não foi implementado aumento automático da taxa após fallback.

## Validação concluída e etapas pendentes

O teste nativo térmico passou, incluindo tempo somente de RAM, atraso cooperativo modelado, prazo, wrap de relógio, polling, quadro bom, descarte por cópia incoerente, máscara e sincronização de poses. O teste do driver IMU passou, incluindo persistência do último evento de invalidação, leitura incompleta, lacuna e gravidade inválida. Os testes puros do LOD passaram nos exemplos citados. O ensaio de escalonamento durante giro depende do equipamento real; testes nativos não simulam essa carga completa.

O firmware `esp32s3` compilou com **194.980 bytes de RAM e 882.961 bytes de flash**. A gravação terminou com verificação dos blocos. A imagem da aplicação foi preservada em `Builds/ScannerESP32S3-thermal-priority-lod-20261004.bin`, **883.328 bytes**, SHA-256 `5dbbc77d8b90154e3f5e22d0269e50290ed42830f1625dc7d22dd1fa30b73a83`.

Após essa gravação, o registro passivo com motores parados mostrou alvo 8, cerca de **7,8 quadros/s**, cópia RAM de **42–43 ms** e duração total de **52–53 ms**. O contador de overruns permaneceu em 2 nas consultas, sem novos erros I²C ou timeouts. Esse resultado confirma aquisição em repouso, não sustentação durante varredura.

**Não houve novo teste com motor nem comando de giro enviado nesta etapa.** O usuário corrigiu que a alimentação principal estava desligada e não podia ligá-la com o USB-C conectado. A observação passiva e a gravação do firmware respeitaram essa condição.

No fechamento desta revisão do relatório, as validações Unity e a geração do novo APK estavam em execução. **Instalação do APK pendente de reconexão do celular; resultado físico do LOD, torção e cadência com motor pendentes.** O agente principal atualizará aqui os resultados efetivamente concluídos, sem inferir aprovação física a partir de compilação.

Fontes reproduzíveis: `thermal-scan-summary.json`, `thermal-scan-audit.md`, `imu-summary.json`, `imu-audit.md`, `pan-axis-imu-estimate.json` / `.md`, `thermal-priority-ram-test.log`, `imu-driver-invalidation-test.log`, `firmware-build.log`, `firmware-upload.log` e `after-priority-upload-serial.txt`, em `diagnostics/20261004/scan-1730-investigation/`. Os relatórios anteriores foram preservados como histórico.

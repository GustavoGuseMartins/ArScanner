# Tempo até fixar o eixo: auditoria de 02/10/2026

## Resultado

Há uma correção pequena e sustentada pelos logs: uma resposta HTTP atrasada apagava a localização inteira, embora o scanner estivesse parado e o AR continuasse rastreando. Na sessão de 20:40, o aplicativo descartou **62 amostras e a altura já observada**; o status voltou apenas **0,56 s depois**. Preservar esse trabalho durante a espera evita repetir etapas, mantendo a captura bloqueada enquanto o estado está antigo.

O resultado visual bom relatado pelo autor aconteceu **depois da gravação**, com um novo perfil de alcance salvo pelo aplicativo e sem mover a montagem. O relato confirma o alinhamento observado; a gravação anterior e os logs não fornecem uma referência física independente em centímetros. A direção manual e os limites atuais do estimador foram preservados.

## Evidência e método

Fontes originais em `diagnostics/20261002/latest`: 12 CSVs UWB, seis registros `Scanner_*.jsonl`, `UwbCalibrationEvents/events.csv`, gravação e screenshot posteriores. A leitura temporal reproduzível está em `diagnostics/20261002/analyze_calibration_timing.py`, com saída `calibration-timing.json`. Quatro linhas finais truncadas foram excluídas; os originais não foram alterados. Os horários abaixo são de São Paulo, UTC−3; os nomes dos arquivos usam UTC.

O relatório principal [análise dos últimos testes](analise-testes-2026-10-02.md) contém a análise visual e a sequência da perda de conexão. A [análise dos perfis](../diagnostics/20261002/profile-analysis.md) compara ciclos distintos e aplica ambos os perfis aos mesmos alcances brutos. Linhas com o mesmo estado ou a mesma candidata não são soluções independentes.

## Cronologia das sessões AR

Os tempos de cada marco partem da primeira linha do CSV da sessão, não do toque que abriu o AR.

| Início da sessão | Apoio observado | Primeira pose multivista confirmada | Direção/captura liberada | Scan confirmado | Observação |
| --- | ---: | ---: | ---: | ---: | --- |
| 20:36:19 | 9,44 s | — | — | — | Apenas quatro amostras retidas; insuficientes para multivista. |
| 20:36:47 | 14,59 s | 17,79 s | — | — | Sem direção; timeout apagou 20 amostras e apoio às 20:37:32,745. |
| 20:38:00 | 7,56 s | 23,63 s | 61,93 s | 64,79 s | Eixo fixado UWB e direção manual; houve captura na gravação. |
| 20:40:08 | 10,01 s | 114,40 s | 140,03 s | 140,37 s | Timeout apagou 62 amostras; a captura final usa marcação manual do apoio. |
| 20:43:17 | 14,85 s | 16,08 s | 71,76 s | 72,00 s | Mais tarde o operador passou à marcação manual; esse scan não confirma o UWB. |
| 20:47:46 | 6,11 s | 16,62 s | 47,22 s | 55,87 s | Após novo perfil; tentativa boa relatada pelo autor, posterior à gravação. |

Na sessão final, a primeira pose confirmada ainda não era a origem que foi fixada:

| Horário | Decisão | Origem AR (X; Y; Z), m |
| --- | --- | --- |
| 20:48:02,724 | Primeiro consenso, oito amostras | (−0,5422; −0,0837; 1,2743) |
| 20:48:12,652 | Refino visual horizontal, limitado a 25 cm | (−0,2975; −0,0837; 1,2231) |
| 20:48:19,143 | Novo consenso multivista, 16 amostras | (0,0484; −0,0837; 1,1534) |
| 20:48:23,115 | Operador fixa o eixo | (0,0484; −0,0837; 1,1534) |
| 20:48:33,324 | Direção manual e deslocamento tag→eixo | (0,0304; −0,0837; 1,1786) |

Portanto, foram aproximadamente **33 s até a última posição calculada, 37 s até a fixação e 47 s até posição/direção prontas**. As mudanças da tabela são entre estimativas do app, não erros medidos contra o eixo físico. A conferência e os toques do operador fazem parte desse intervalo; não há evidência de 31 s de processamento após o primeiro consenso.

## Recalibração dos alcances

O perfil anterior tinha escalas 1,035396 / 1,027920 / 1,010342. O novo, salvo às 20:47:41,920, tem 0,769570 / 0,919901 / 0,820569 e offsets −0,266894 / −0,633963 / −0,347418 m. Essa é uma mudança material na correção das distâncias, coerente com o fato de que recalibrar alterou o resultado. Não identifica sozinha a origem do viés.

A primeira captura começou às 20:46:10,249 e o salvamento aconteceu 91,67 s depois. Foram três capturas do ponto próximo e uma do distante, todas aceitas, com aquisição total de **9,37 s** (2,27–2,44 s cada). A maior parte desse tempo foi entre capturas, ajuste físico e preenchimento; reduzir as 20 leituras não resolveria a maior parte da espera.

O código guarda o perfil v2 até redefinição explícita e o carrega ao iniciar a cena (`UwbRangeCalibrationProfile.Load`, `UwbAnchorManager.Start`). Os logs mostram o mesmo perfil em todas as sessões anteriores e o novo na última. Não houve expiração silenciosa de perfil nestes testes. Não há razão para exigir recalibração em cada abertura, apagar o perfil que funcionou ou trocar atrasos de antena sem medida controlada.

## Causa demonstrada: status antigo tratado como perda espacial

`PointCloudTcpReceiver.HasFreshStatus` exige resposta com menos de 2,5 s. O polling normal é de 1 s; a consulta de status tem timeout de 2 s. Câmera e consultas de diagnóstico serializam suas requisições com o status (`PointCloudTcpReceiver.Update`).

Antes desta correção, `UwbAnchorManager.UpdateAutomaticUwb` chamava `SuspendAutomaticPose` e `ResetAutomaticHistory` imediatamente se o status expirasse. O reset limpa apoio, consenso, fixação, multivista, correção visual e histórico AR. Esse comportamento é apropriado para perda do mundo AR, mas uma resposta HTTP antiga não comprova que a base ou o referencial AR tenham mudado.

Evidência concreta:

- **20:37:32,745:** 20 amostras e apoio descartados; AR continua ativo. O registro HTTP seguinte informa falha de conexão.
- **20:41:53,421:** 62 amostras e apoio descartados; o HTTP retorna às 20:41:53,981 com o mesmo pan parado, passos 18.222 e referência válida. O reset forçou nova coleta sem mudança física evidenciada nesse intervalo.
- **20:49:20,831:** ausência de status durante a captura apaga fixação/direção já aceitas. A origem numérica da nuvem permanece, mas a referência necessária à captura é perdida.

A terceira interrupção depois evoluiu para uma condição diferente: às **20:50:42,815**, a conexão volta com referência do pan `interrupted`, `panReferenceValid=false`, passos zerados e relógio do scanner reiniciado. O autor relatou queda da rede/conexão; não informou reinicialização voluntária. A reinicialização é compatível com esses campos, mas sua causa permanece indeterminada. A perda real do zero deve continuar bloqueando a captura e pedindo confirmação física.

## Correção aplicada nesta revisão

Em `Assets/Scripts/Spatial/UwbAnchorManager.cs`:

1. Um status anteriormente observado, com pan finito, pode ficar antigo sem apagar o trabalho de uma base declarada imóvel. Enquanto espera, o app não processa novos alcances. Fixação e aceitação de pontos continuam exigindo `HasUsablePanReference`, que exige status recente. A mensagem de captura pausada significa que o app suspende a aceitação de novos pontos; ela não envia um comando para parar o motor durante a falta de comunicação.
2. Ao recuperar o status, se o pan mudou mais de 2° e a origem ainda não foi fixada, descarta a coleta anterior: a tag está na cabeça móvel e pode ocupar outra posição. Uma origem fixada é o eixo do pan; girar a cabeça não desloca essa origem.
3. Status ausente sem observação anterior, pan inválido, perda do rastreamento ou mudança do referencial AR mantêm o reset. Perda real da referência do pan invalida a direção, mesmo se sua origem estiver fixada. Restaurar o zero sozinho não restaura o yaw AR.
4. O guia passa a mostrar primeiro falta de rastreamento, estado recente, perfil ou zero. Para a altura, distingue plano não encontrado, profundidade ausente, superfície incompatível, divergência visual/UWB e enquadramento já válido aguardando um segundo. Os limites geométricos permanecem iguais.

Não foram alterados a rotação manual, os sinais mecânicos, a altura nominal, a geometria das antenas, o perfil salvo, o resíduo aceito ou o congelamento da origem com pontos existentes. O slider de opacidade é tratado na revisão de interface paralela.

## Esperas necessárias e melhorias seguintes

O algoritmo usa pausa de 0,6 s após mais de 2 cm/3° de movimento; confirmação de apoio de 0,8 s; pelo menos oito ciclos distribuídos em posições distintas; máximo de quatro amostras próximas; separação horizontal mínima de 35 cm com altura conhecida; consenso em três ciclos novos por pelo menos 0,4 s. A confirmação por ciclos independentes já funciona após `position_full`. As regressões de apoio expirar ou exigir outra vista para confirmar a mesma geometria já haviam sido corrigidas em 01/10; não reaplicar essas mudanças.

A confirmação não avança no ciclo que acaba de gerar a candidata. Ao continuar caminhando, o operador reinicia pausas ou atualiza a candidata; terminar uma pausa permite acumular os ciclos seguintes. O guia existente recomenda dois segundos. É mais útil indicar quando a pausa já está completa e quando falta outra vista que enfraquecer esses limites. As novas mensagens ajudam a distinguir o apoio ausente de um problema de conexão, sem mudar as evidências exigidas.

Para a validação automática de X/Z usando só as peças existentes, ver [avaliação da posição automática](avaliacao-posicao-automatica-2026-10-02.md). O refino atual usa superfícies próximas, preserva uma margem de 12 cm e limita a correção a 25 cm; não reconhece por si só o centro do eixo. A tampa original e sua geometria podem fornecer uma verificação independente após o yaw manual, mas esse reconhecedor permanece experimental. Não foi habilitado como requisito do fluxo que funcionou.

## Verificação

`Assets/Editor/StationaryPoseValidation.cs` recebeu regressões no caminho real `Update`: status antigo preserva origem e bloqueia captura; recuperação permite retomar sem mover eixo fixado; referência de pan realmente perdida continua invalidando yaw; retorno do zero sozinho não restaura yaw; pan NaN mantém reset; coleta não fixada é preservada durante espera e descartada se a cabeça mudou; AR perdido mantém reset completo. A checagem de alterações nos arquivos passou. A suíte completa `ScannerPoseValidation`, incluindo estas regressões, passou no Unity pelo fluxo principal desta revisão, registrado em `diagnostics/20261002/validated-build.log`. A validação de software não substitui o ensaio físico do tratamento de interrupção.

O ensaio físico seguinte deve usar o perfil que funcionou, repetir localização e direção sem mover a montagem, comparar tempo até a posição final fixada e induzir apenas uma interrupção de rede curta. O app deve pausar, mostrar o motivo e retomar sem recolher as mesmas vistas. Perda de referência real deve continuar exigindo confirmação física e novo yaw. Só a comparação física do eixo permite quantificar o erro X/Z; sigma e repetição interna são verificações de consistência.

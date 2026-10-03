# Últimos testes: calibração, vídeo e posição do eixo — 02/10/2026

## Conclusão e escopo

O resultado bom relatado pelo autor ocorreu depois da gravação, após recalibrar os alcances UWB pelo aplicativo. O autor confirmou que não reposicionou a montagem para resolver o alinhamento. Os registros permitem separar três etapas: calibração dos alcances, localização da tag/eixo no AR e alinhamento manual da direção. Não há justificativa para alterar os sinais, a rotação manual, a geometria do LiDAR ou diminuir os requisitos de aceitação do estimador.

Uma falha curta de leitura do estado do scanner apaga indevidamente o progresso espacial no código anterior. A correção desta revisão deve preservar esse progresso durante a espera e continuar suspendendo a aceitação de pontos enquanto o estado está desatualizado. Perda real do referencial AR ou do zero do motor continua exigindo validação.

O slider de opacidade foi recolocado no painel normal da nuvem, perto de Exportar/Limpar, mantendo a faixa de 10–100%. O renderizador e o shader já aplicavam o alpha; não foi necessário mudar os materiais nem o processamento dos pontos. As superfícies agrupadas pelo LOD continuam atualizando em até dois segundos, comportamento existente.

## Evidências preservadas

- Gravação original: `diagnostics/20261002/latest/Screen_Recording_20261002_204517_ScannerAR.mp4`, 432.173.704 bytes, duração de 389,19 s. Copiada do celular conectado, sem remover o original.
- Screenshot posterior: `Screenshot_20261002_205112_ScannerAR.jpg`, com 29.781 pontos e referência do pan indisponível.
- Doze CSVs `Uwb_20261002_*.csv`, seis arquivos `Scanner_20261002_*.jsonl` e `UwbCalibrationEvents/events.csv`, copiados do armazenamento do aplicativo.
- Quadros amostrados e folha de contato em `diagnostics/20261002/latest/frames` e `contact-sheet.jpg`.
- O buffer de log do Android já não continha a cronologia Unity do ensaio; as conclusões temporais usam os arquivos persistentes do aplicativo.

Os CSVs podem repetir o mesmo ciclo de rádio em eventos de chegada/decisão. As análises anexas distinguem linhas registradas e ciclos únicos e descartam linhas finais truncadas. Nomes de CSV/eventos usam UTC; os horários neste relatório são de São Paulo (UTC−3).

## O que aparece no vídeo

O vídeo contém tanto capturas quanto tentativas de corrigir uma origem que ainda não coincide com o scanner. Portanto, o problema não era uma impossibilidade permanente de escanear. O melhor alinhamento informado pelo autor não aparece nessa gravação.

Quadros relativos ao início da gravação:

| Tempo aproximado | Observação direta |
| --- | --- |
| 7,5 s | Eixo já fixado, direção ainda desconhecida e nuvem vazia. |
| 22,5–67,5 s | Captura e pontos visíveis no ambiente. O usuário abre o diagnóstico e depois pausa. |
| 82,5–187,5 s | Nova tentativa com nuvem vazia. A interface alterna apoio não confirmado, coleta multivista e posição ainda aproximada; o botão de fixação fica indisponível em parte do intervalo. |
| 202,5 s | A interface declara eixo UWB estável, mas a vertical verde aparece afastada do scanner. Estabilidade interna não certifica posição física. |
| 217,5–262,5 s | Alinhamento manual e nova captura. |
| 292,5–337,5 s | Nova localização; há uma candidata divergente e exigência de revalidação. |
| 352,5 s | Outra captura, seguida da saída para o menu. |

A folha de contato usa amostragem a cada 15 s, e não registra todos os toques. O vínculo entre relógio real e tempo de vídeo é inferido pela sequência dos scans: o nome/metadado 20:45:17 corresponde ao fim da gravação, com início aproximado às 20:38:48. Não usar essa inferência como medição quadro a quadro.

## Tempo da tentativa boa

O novo perfil foi salvo às **20:47:41,920**. A sessão AR seguinte começa nos registros às **20:47:46,103**. Nela:

| Marco | Tempo desde início da sessão AR |
| --- | ---: |
| Apoio reconhecido | 6,11 s |
| Primeira posição aceita, ainda sujeita a refinamento | 16,62 s |
| Refinamento pela superfície observada | 26,55 s |
| Última solução UWB antes da fixação | 33,04 s |
| Eixo fixado pelo operador | 37,01 s |
| Direção manual alinhada / captura liberada | 47,22 s |
| Scan confirmado | 55,87 s |

O primeiro candidato de 16,62 s ainda não representa o eixo final. Houve um refinamento visual de 25 cm aos 26,55 s e uma nova solução UWB aos 33,04 s; o operador fixou a origem aos 37,01 s. Portanto, o ponto final exigiu aproximadamente 33–37 s, e a direção veio cerca de dez segundos depois da fixação. Esses intervalos incluem interação e conferência do operador; não são latência computacional pura. Não remover a confirmação manual que produziu o resultado bom para tentar diminuir esse tempo.

A recalibração no menu levou 91,67 s desde o primeiro toque de captura até salvar. As quatro coletas (três repetições perto e uma longe) consumiram apenas 9,37 s no total, cerca de 2,3 s cada. Diminuir o número de medidas não atacaria a maior parte da demora. O perfil novo permanece salvo para outras sessões; não é necessário refazer as duas distâncias a cada abertura do AR.

No conjunto de snapshots distintos e finitos do estimador, o resíduo mediano passou de 7,09 cm com o perfil anterior para 3,98 cm com o novo (1.163 e 131 snapshots, respectivamente). Isso é uma diferença interna de ajuste entre conjuntos de ensaios, com diferentes qualidades e vistas; não é um erro físico medido do eixo. A comparação algébrica entre perfis nos mesmos alcances também está registrada em `../diagnostics/20261002/profile-analysis.md`. O novo perfil não elimina todas as inconsistências entre alcances, e essa estatística não justifica trocar componentes ou afrouxar critérios.

Uma conferência opcional do perfil salvo em uma distância intermediária de 1,5 m, com a placa centrada e de frente, ajuda a verificar a interpolação sem gravar outro perfil. Isso confere alcance, não a posição X/Z ou a direção. Para uso normal, primeiro observe o apoio parado e depois faça pausas de aproximadamente dois segundos em vistas realmente separadas; movimentar continuamente o celular ou repetir a mesma vista não fornece a geometria necessária.

## Interrupções diferentes exigem respostas diferentes

Às **20:41:53,421**, uma leitura de estado desatualizada apagou **62 amostras e o apoio observado**. O HTTP voltou às **20:41:53,981**, cerca de 0,56 s depois. O código tratava falta temporária de estado como perda da referência espacial. Essa é uma causa demonstrada de trabalho repetido desnecessariamente, independentemente da qualidade do perfil UWB.

Depois da tentativa boa, às **20:49:20,831**, outro período sem estado apagou posição/direção durante a captura. Posteriormente os registros voltam às **20:50:42,815** com `panReferenceValid=false`, `panReferenceState=interrupted`, passos zerados e relógio do scanner reiniciado. O autor informou que a rede/conexão do scanner caiu, sem informar uma reinicialização voluntária. Essa perda do zero é real; o screenshot de 20:51 não é apenas uma mensagem causada por status antigo. Os registros não determinam a causa da reinicialização. A correção da espera HTTP não autoriza ignorar essa condição.

As leituras também mantêm IMU indisponível e zero quadros térmicos, com erros de transporte I2C. Esta revisão não modifica a aquisição desses sensores. Eles não são uma medição independente que possa certificar X/Z, nem explicam por si só o acerto após recalibrar UWB.

## Revisão elétrica/mecânica e automação de X/Z

Ver `avaliacao-posicao-automatica-2026-10-02.md` para netlists, medidas da base, tampa e peças móveis, limites do centro da antena e fontes oficiais. Há uma câmera do celular disponível para observar uma referência física, sem modificar a placa. Uma tag UWB e três alcances não fornecem por si só a orientação da base, e um plano AR do apoio não identifica o centro do eixo.

O autor escolheu usar **somente as peças atuais**, sem alvo impresso adicional. É possível usar a tampa original de 68 mm como verificação visual de X/Z, descontando sua posição relativa ao eixo com o yaw manual já aceito. O reconhecedor existente está experimental e exige múltiplas vistas; ativá-lo obrigatoriamente pode acrescentar demora. O próximo desenvolvimento indicado é comparar a posição visual pela tampa com a origem UWB já aceita, sem substituir a direção manual. Nenhuma alternativa visual foi promovida automaticamente a fonte de pose nesta revisão.

## Análises detalhadas e validação

- `auditoria-tempo-calibracao-2026-10-02.md`: cronologia por sessão, resets, critérios e correção do tratamento de estado temporariamente ausente.
- `../diagnostics/20261002/profile-analysis.md`: análise reproduzível dos perfis antigo/novo, ciclos únicos e limites da interpretação.
- `../diagnostics/20261002/validated-build.log` e `viewer-playmode.log`: suíte completa e cena real em Play Mode aprovadas, incluindo preservação sob status antigo, bloqueio de pontos e atualização de alpha em pontos existentes pausados.
- `../Builds/ScannerAR-calibration-opacity-20261002.apk`: 36.420.281 bytes; SHA-256 `5e699049f12fa9a534b7e6d57bc29bcbb7e1783de2ee2a1470f43a73ebc8111d`. Assinatura igual à versão anterior. Firmware não modificado.
- `../diagnostics/20261002/delivery.json`, `apk-install.log`, `installed-startup.log` e `installed-menu.png`: atualização instalada com sucesso no Samsung SM-S916B; lançamento a frio aprovado em 424 ms e menu conferido visualmente com **perfil salvo**.

A atualização preservou os 86 CSVs UWB e o hash do histórico de calibração do aparelho. O menu informa que scanner e base USB não estão conectados neste momento; não houve nova captura, comando de motor ou recalibração nessa verificação. A transparência foi verificada na integração Unity, e não com uma nova nuvem física no celular.

A diferença visual relatada pelo autor é evidência de uma tentativa bem-sucedida. Ela não substitui uma medida física de erro X/Z em centímetros. O benefício de tempo da correção de software deve ser confirmado repetindo sessões novas no aparelho, mantendo o perfil que funcionou.

# Aproveitamento do hardware existente — 03/10/2026

## O que vale aproveitar primeiro

O conjunto que deu a nuvem boa — LiDAR, motor de pan, transmissão e UWB com calibração de alcance — está sendo usado. **O GY-25 passou a fornecer leituras válidas no firmware v11 observado em 03/10:** identidade MPU6050 confirmada, calibração do giroscópio concluída e zero erros de leitura durante a observação de 20 segundos. A oportunidade agora é conferir os eixos e a cadência dessas medidas antes de usá-las para verificar movimento/inclinação, preservando o alinhamento manual que funcionou. Também podemos registrar melhor a qualidade das medidas UWB e LiDAR.

O autor confirmou inicialmente que o **GY-25 estava em SDA/SCL, com o seletor aberto**. O acesso direto à MPU6050 foi observado nessa condição, mas isso não certifica a seleção física da interface nem que o processador interno do módulo deixou de atuar. **Correção de 03/10:** a leitura curta não dispensa o requisito do manual. No GY-25 nele descrito, deve-se fechar a ponte de seleção I²C, que bloqueia a serial e permite acesso direto à MPU6050. Primeiro identificar precisamente o módulo e a ponte; se corresponderem ao manual, fechá-la com todas as alimentações desligadas e repetir o teste compartilhado com a térmica. Não foi demonstrada outra variante nem uma causa única para as falhas de 02/10. [Manual do fabricante GY-25](https://www.gysensor.cn/wp-content/uploads/GY-25%E5%80%BE%E6%96%9C%E8%A7%92%E5%BA%A6%E4%BC%A0%E6%84%9F%E5%99%A8%E6%A8%A1%E5%9D%97%E4%BD%BF%E7%94%A8%E6%89%8B%E5%86%8C2.pdf).

**Atualização após a solda em 03/10:** o usuário informou ter fechado as duas pontes. A observação USB confirmou MPU e térmica lendo juntas por cerca de 50 s: MPU pronta/identidade 0x68/bias, zero erros e zero lacunas observadas; térmica 340→535 quadros, sem novos erros, leitura de 51–53 ms. A ponte esquerda é seleção de velocidade UART e não altera o acesso I²C. O resultado e a conferência da Figura 1 do fabricante estão em [validação após a solda](validacao-gy25-pos-solda-2026-10-03.md). Os registros abaixo de 174 ms e lacunas referem-se ao ensaio anterior.

**A térmica voltou a produzir quadros nas observações USB de 03/10.** Antes do v11, o registro passivo da COM6 mostrou `ready_partial`, quadros de 561 para 591 em 15 segundos e idade de 304–305 ms. Após a gravação v11, continuou com quadro disponível enquanto montava o seguinte (`assembling`, `frame=1`), quadros de 39 para 69 em 15 segundos, 18 pixels excluídos e aviso de calibração `-4`. Os contadores de erro I2C, timeout e overrun permaneceram constantes nos respectivos intervalos. Isso comprova aquisição parcial atual; não comprova precisão absoluta nem fusão térmica durante um scan.

A inspeção de hardware descrita nesta auditoria não alterou fios, peças ou código. Em uma frente de implementação da mesma sessão, o responsável principal gravou o firmware v11 com os motores parados; seus resultados físicos estão incorporados aqui. A compensação de inclinação continua desligada. A preferência do autor é usar somente as peças atuais, sem acrescentar alvo visual. O ensaio completo continua dependendo da alimentação principal, que ele não pode ligar nesta sessão.

## Como separar os estados

| Estado | Significado nesta nota |
| --- | --- |
| Funcionando no teste | Há observação física ou registro da operação; a data e o limite são indicados. |
| Implementado, desativado | O caminho existe, mas a configuração atual o impede. |
| Apenas diagnóstico | Mede ou registra algo sem participar da pose/nuvem. |
| Sem ligação disponível | A netlist não liga a função ao processador; não basta ativar uma opção. |
| Modo alternativo | Há outro programa/modo, que não opera junto com o conjunto atual como está. |
| Não confirmado | A disponibilidade física ou a precisão ainda não foi medida. |

Uma indicação `Ready`, uma resposta ao endereço I2C e uma medida útil são coisas diferentes. A resposta ao endereço não comprova identidade, leitura completa, calibração ou sequência de amostras.

## Evidências dos últimos testes

Os seis arquivos `Scanner_*.jsonl` da captura de 02/10 contêm **640 registros**, versão de diagnóstico 10, entre 20:36:19 e 20:51:48 de São Paulo. São amostras periódicas de estado, não 640 leituras independentes de cada sensor.

| Subsistema | Captura do celular em 02/10 | Observação posterior / limite |
| --- | --- | --- |
| LiDAR e pan | Scan ativo em 158 registros; RPM LiDAR atingiu 308,31; PWM 160 e pan configurado em 2 RPM. | Há uso real. Esses máximos não medem estabilidade de velocidade nem precisão do motor. |
| UWB tag | `tagReady=true` nos 640 registros; contadores de pedidos e respostas aumentaram nas sessões. | Está em uso. O bom resultado relatado veio após recalibrar o alcance; não justifica trocar os parâmetros que funcionaram. |
| Referência de pan | Válida em 574 registros; 66 indicaram interrupção. | A contagem de passos é usada. A interrupção não identifica sua causa elétrica/mecânica. |
| GY-25/MPU | `imuReady=false` e `imuCalibrated=false` nos 640; ACK em 411, sem ACK em 229. | Após firmware v11 em 03/10: `ready=1`, identidade 104 (`0x68`), bias calibrado, uma tentativa de inicialização e zero erros de leitura em 20 s. Idade observada 174 ms; eixos e cadência para fusão ainda precisam de análise. |
| MLX90640 | `thermalReady=false`, zero quadros e zero pontos térmicos nos 640; ACK em 513, sem ACK em 127. | Em 03/10 houve aquisição parcial fresca na COM6. Não classificar a térmica como permanentemente sem uso com base em 02/10. |
| RGB do scanner | `rgbReady=false`, perfil 0 nos 640. | Continua desativada no programa normal; a atribuição de cor real aos pontos ainda não está implementada. |

A aquisição térmica também teve sucesso histórico em 01/10, com 750 pixels elegíveis e 18 mascarados. Os coeficientes desses 18 pixels não são inventados nem preenchidos por interpolação. A recuperação parcial não certifica a exatidão dos outros pixels. Em 02/10 houve uma sessão de bancada somente por USB sem quadros; a nova observação de 03/10 mostra que essa condição não implica ausência permanente de alimentação dos sensores.

Evidências locais: [análise da captura de 02/10](C:/Users/aaata/Projetos/tcc/ArScanner/docs/analise-testes-2026-10-02.md), [histórico da aquisição térmica](C:/Users/aaata/Projetos/tcc/ArScanner/docs/analise-termica-2026-10-01.md), [observação USB antes do v11](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/usb-observation.log), [primeira observação após v11](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/usb-v11-observation.log) e [observação final v11 com eixos e lacunas](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/usb-v11-final.log).

### Leitura final v11: aquisição observada, com lacunas de integração

Após a gravação final v11, com motores parados, cinco linhas periódicas de diagnóstico mostraram MPU pronta, identidade `0x68`, bias calibrado, uma inicialização, zero erros de leitura e tilt desligado. A aceleração ficou próxima de `(-0,934; -0,016; 0,290)` g, com norma de aproximadamente 0,979 g; o giroscópio ficou próximo de zero nas amostras impressas. O ângulo Y do **sensor** ficou em 71,64–71,93°. Esse ângulo não é a inclinação calibrada do scanner no mundo AR: a transformação da montagem ainda precisa de ensaio.

Os últimos intervalos impressos foram 6.912–7.073 µs, mas **isso não demonstra uma taxa contínua de aquisição**. No mesmo período, o contador de lacunas passou de 260 para 340 em 20 segundos: 80 intervalos recusados para integração, cerca de quatro por segundo. A idade no momento da impressão foi 173–174 ms. A térmica registrou leitura de 174 ms, quadros de 129 para 169 no mesmo intervalo, quadro disponível e máscara de 18 pixels mantida. Esses dados, junto à ordem sequencial da tarefa compartilhada, quantificam a limitação temporal atual; não identificam a causa única de todas as falhas anteriores. Não foi demonstrado que essas lacunas resultem da ponte aberta ou de conflito com o processador interno da GY-25.

O firmware e seus diagnósticos foram gravados/verificados pelo responsável principal, em duas gravações v11 com hashes conferidos. O [APK de diagnóstico GY-25](C:/Users/aaata/Projetos/tcc/ArScanner/Builds/ScannerAR-gy25-diagnostics-20261003.apk) foi gerado e os testes Unity passaram; ele **não foi instalado**, pois não havia celular conectado por ADB. O resultado físico acima é do firmware via USB, não de uma sessão de captura com esse APK.

## Inventário elétrico: todas as peças das duas netlists

`tcc.net` contém 25 componentes e `tccvis.net`, 14. Conectores e componentes passivos estão agrupados abaixo para facilitar a leitura. GY-25, câmera e detalhes internos dos módulos de alimentação não aparecem como circuitos completos nessas netlists; sua identificação também depende da montagem/documentação.

| Peça / referências | Uso real e o que ainda não aproveitamos |
| --- | --- |
| ESP32-S3 do scanner — U1 | Rede Wi-Fi do scanner, pontos por TCP, comandos, diagnósticos, aquisição e contagem do pan. A memória persistente guarda referência/estado do pan e opções térmicas. Não há medição de tensão da bateria ou corrente na implementação. Pinos sem ligação na placa principal não provam disponibilidade na placa de câmera ou no próprio DevKit. |
| DWM1000 da cabeça — DWM1 | Uma tag fornece alcances UWB. SPI usa GPIO4/5/41/42; IRQ usa GPIO2, mas o programa trabalha por consulta e desliga a interrupção. Reset, WAKEUP, EXTON e GPIO7 não estão ligados. Os demais GPIO auxiliares do rádio estão aterrados na netlist; não são sensores extras disponíveis. |
| MLX90640 — A1 | Aquisição, máscara, prévia e associação de temperatura a raios estão implementadas. A aquisição parcial está funcionando na observação de 03/10. A fusão exige quadro recente, alinhamento e campo de visão; ela não participou dos pontos registrados em 02/10. |
| LiDAR LDS02RR informado pelo autor — J3 | Distância, índice angular, velocidade informada pelo sensor, validade e checksum são usados. A revisão v12 agora preserva intensidade no CSV e aviso de sinal fraco no TCP/CSV, com rejeição opcional no aplicativo. Nenhum corte de distância novo foi acrescentado. A ligação TX do ESP existe; não foi encontrado uso de comandos ao sensor no fluxo atual. A identificação vem do autor/configuração, sem inspeção da etiqueta nesta revisão. |
| Motor DC do LiDAR — Q1, D1, R1 e R3 | Q1 comanda o motor por PWM; R1 limita o sinal de gate, R3 mantém seu estado de repouso e D1 participa da proteção da carga. São usados no circuito, não capacidades sobrando. A velocidade é observada, mas o PWM atual não forma um controlador automático de RPM. |
| Motor de pan — M1 e TMC2209 U3 | STEP/DIR, rampa, velocidade, modos 180°/360°, teste 2D, estacionamento e histórico de passos estão implementados. Sem encoder ou fim de curso. UART, DIAG, INDEX, MS1/MS2, PDN e SPRD não estão ligados ao ESP na netlist. EN está aterrado; parar os pulsos não equivale a retirar corrente do motor. |
| I2C / desacoplamento — R2, R4, C2, C3 e C4 | Pull-ups e capacitores da alimentação de sensores. Usados passivamente; não falta uma função de software para aproveitar essas peças. MLX e GY compartilham SDA47/SCL48. |
| Alimentação e interfaces — C1, J1, J2, J4, J5 e J6 | J1 distribui +12 V, J2 terra; J4/J5 representam ligações de alimentação externa e J6 o módulo auxiliar. C1 é reserva/desacoplamento, sem valor indicado. Os reguladores externos e o caminho físico de alimentação por USB não são definidos por completo. J6 liga alimentação, GND, SDA e SCL; suas outras quatro vias não têm ligação na netlist. |
| Fixação — H1–H4 do scanner | Pads ligados a GND. Uso de fixação/terra, sem função de medição. |
| ESP32-WROOM da base — U1 em `tccvis.net` | Consulta os três rádios e envia alcances/diagnóstico pela USB ao celular. O Wi-Fi/UDP descrito no arquivo de configuração não é iniciado pelo programa atual. Não é necessário ativá-lo para aproveitar os rádios; mudaria a arquitetura de comunicação. |
| DWM1000 da base — DWM2, DWM3 e DWM4 | Os três rádios são usados, com seleção individual, SPI compartilhado e reset conjunto. IRQ está ligado a GPIO34/35/39, mas as interrupções são desativadas deliberadamente no programa de consulta. WAKEUP, EXTON e GPIO7 não estão ligados; GPIO auxiliares restantes estão aterrados. |
| Capacitores e fixação da base — C1–C7 e H1–H3 | Reserva/desacoplamento da rede comum de 3,3 V e fixação aterrada. Sem canal adicional de sensores ou medição da bateria. |

### Alimentação: o que o desenho diz e o que o teste mostrou

Na netlist do scanner, `3V3` liga somente o pino 3,3 V do ESP. A rede separada `Net-(A1-VDD)` liga MLX, DWM1, alimentação lógica do TMC, pull-ups e J6.1, chegando a J4.3. Não há ponte desenhada entre essas duas redes. A rede de 5 V liga o DevKit, J3 e J5.3. Assim, **o desenho sozinho não garante que USB alimente todos os sensores**.

Porém, as observações de 03/10 comprovam que a MLX e, após v11, a MPU6050 estão recebendo alimentação suficiente para aquisição nessa montagem, mesmo com a principal informada como desligada. Não foi identificado o caminho de alimentação real: cabos, reguladores e ligações internas dos módulos não estão totalmente representados. Não concluir defeito, retroalimentação específica ou necessidade de alterar fios somente a partir da netlist. O teste por USB permite ler estados e aquisição atual, mas não reproduz as condições de motor e alimentação do scan completo.

## GY-25: capacidade disponível e uso atual

O firmware acessa diretamente uma MPU6050 em I2C, endereço `0x68`, verificando `WHO_AM_I=0x68`. Lê aceleração e giroscópio nos três eixos, calibra o desvio do giroscópio por cerca de três segundos imóveis e registra idade/intervalo das amostras. O v11 confirmou leitura e calibração no equipamento. A compensação geométrica de inclinação está **desativada** (`IMU_APPLY_TILT=false`); não altera os pontos bons atuais. O yaw interno existe, começa em zero e não participa do alinhamento AR/UWB.

O driver deixa de integrar o giroscópio quando o intervalo ultrapassa 100 ms, evitando estender uma amostra antiga por todo o atraso. A coleta final confirmou aproximadamente quatro dessas lacunas por segundo com a térmica ativa. Portanto, `ready=1` e o último intervalo curto não bastam para considerar yaw contínuo confiável. A aquisição precisa preservar a sequência de amostras antes de promover o giroscópio a acompanhamento contínuo de direção.

O GY-25 também oferece uma saída serial com processamento do próprio módulo. Ela é um **modo alternativo**, não implementado pelo driver atual. RX/TX do módulo não estão ligados ao ESP na netlist de J6, e ativar UART exige confirmar e alterar a ligação física. Não confundir GY-25 com GY-25T: o fabricante publica manuais separados. [Página oficial de documentação](https://www.gysensor.cn/download/).

| Informação da MPU / GY | Aproveitamento útil com as peças atuais | Limite |
| --- | --- | --- |
| Aceleração nos três eixos | Conferir repouso, vibração e direção da gravidade; detectar que um apoio/pan que deveria estar parado se moveu. | Movimento pode confundir gravidade. Não fornece X/Z absoluto por si só. |
| Velocidade angular nos três eixos | Comparar giro real da cabeça com o pan comandado; acompanhar direção relativa após uma referência inicial. | Requer transformar os eixos da placa para os da montagem, calibrar desvio e considerar idade/lacunas. O Z do sensor não é automaticamente o eixo vertical do scanner. |
| Inclinação por gravidade | Verificar apoio inclinado; futuramente compensar pitch/roll após ensaio físico dos eixos. | A configuração atual não foi validada para aplicar essa correção à nuvem. |
| Yaw relativo | Conservar/observar uma direção inicial durante um intervalo curto. | Não descobre sozinho a direção inicial no mundo AR. O módulo não tem magnetômetro e deriva em yaw. |
| Temperatura interna do chip | Diagnóstico opcional de aquecimento e desvio do giroscópio. Seus bytes já passam pela leitura de 14 bytes, mas são ignorados. | É temperatura do chip, não a temperatura do ambiente ou do objeto escaneado. |
| FIFO | Buffer interno não configurado pelo driver atual; candidato para preservar amostras enquanto a tarefa compartilhada lê a térmica. | Exige recuperar a sequência, reconstruir tempos e tratar overflow/reset; não basta integrar uma amostra pelo atraso inteiro. |
| Data-ready e interrupção de dados | Não utilizados. Consulta do status pode identificar amostra nova; interrupção exigiria conferir a disponibilidade física do pino INT no módulo. | A netlist de J6 não oferece uma ligação INT ao ESP. Interrupção não elimina a ocupação do barramento pela térmica. |
| DMP | Processamento interno não configurado; candidato posterior caso haja benefício medido para a orientação. | Outra implementação/validação; não cria direção horizontal inicial, posição absoluta ou magnetômetro. |

A MPU6050 reúne acelerômetro, giroscópio, sensor de temperatura, FIFO e processamento interno; uma referência magnética dependeria de sensor externo. [Datasheet oficial TDK/InvenSense](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6000-Datasheet.pdf). Para esta montagem, o primeiro uso recomendável é **conferir a pose aceita**, sem substituir imediatamente yaw manual ou posição UWB.

## Informações que já chegam, mas ainda pouco usamos

**Qualidade UWB.** A biblioteca disponibiliza potência recebida, potência do primeiro caminho e qualidade de recepção. A potência já é utilizada internamente na correção de timestamp do driver; portanto, ela não está totalmente ignorada. A lacuna é não registrar essas métricas associadas a cada alcance no diagnóstico da aplicação nem usá-las para indicar condições suspeitas. Registrar primeiro, sem trocar automaticamente calibração/pesos, permitiria comparar tentativas boas com trajetos refletidos/obstruídos. Métricas do DW1000 ajudam a estimar confiança; não certificam ausência de reflexão nem corrigem a geometria das antenas próximas. [Nota oficial Qorvo/Decawave APS006, parte 3](https://forum.qorvo.com/uploads/short-url/v27pcxSy7qGzGqXiucYgtUNnCtF.pdf).

O SPI por software da base é deliberado para o comportamento observado do MISO dessa PCB; as interrupções também foram removidas para evitar conflitos do estado compartilhado do driver. Reativar SPI por hardware/IRQ não é uma melhoria automática e não é prioridade sem uma medição que justifique.

**Qualidade LiDAR.** O formato de 22 bytes contém intensidade e um aviso separado de sinal fraco. Antes desta atualização, esses dados eram descartados pelo parser. A revisão v12 os preserva para diagnóstico e permite comparar a nuvem com a rejeição do aviso ligada/desligada. A opção começa desligada; um retorno fraco pode ter distância válida. A intensidade ainda não recebe limiar arbitrário e nenhum filtro de distância novo foi aplicado, conforme a decisão do autor. [Implementação e limites do filtro](C:/Users/aaata/Projetos/tcc/ArScanner/docs/filtro-qualidade-lidar-2026-10-03.md). A referência é do protocolo adotado, não prova que o sensor instalado possui a IMU ou demais recursos do produto Cullinan. [Formato oficial Roborock LDS](https://github.com/Roborock-OpenSource/Cullinan#lds-serial-data).

**Térmica.** A orientação da imagem e a máscara já estão implementadas. Quando não existe temperatura válida para um ponto, o código usa um valor substituto de 21 °C acompanhado da flag de temperatura ausente; ele não é uma leitura real. Não usar esse número para avaliar funcionamento. Fusão térmica e geometria são etapas independentes.

**RGB.** O perfil normal 0 não configura os pinos. O perfil alternativo de câmera conflita com GPIO4/5 do UWB e desliga a tag no programa de teste. Mesmo nesse modo, a função que atribuiria RGB a cada raio retorna `false`; prévia JPEG não equivale a cor calibrada da nuvem. É uma função incompleta que requer pinagem, alinhamento e desenvolvimento, não apenas mudar uma opção.

**Diagnóstico de alimentação/reinício.** Não há divisor de tensão, sensor de corrente ou leitura de bateria nas netlists/programas examinados. Registrar a causa de reinício do ESP pode ser uma melhoria de diagnóstico por software, mas não substitui medir alimentação sob carga. Capacitores e bornes não fornecem essa leitura.

## Mecânica: o que já é aproveitado e o que não é sensor

| Conjunto existente | Uso / oportunidade / limite |
| --- | --- |
| Engrenagem fixa de 180 dentes e pinhão de 10 | Redução 18:1 usada no firmware. Com motor de 200 passos e configuração nominal de 8 microsteps, são 28.800 pulsos por volta da cabeça. A contagem não detecta passos perdidos ou giro à mão. Não alterar redução/sinal que deram resultado bom. |
| Rolamento 6202, mesa, quadro e suporte anti-torque | Apoio e rotação mecânica. Não há posição absoluta ou encoder escondido nesses componentes. |
| Slip ring | Modelo de 20 vias, corpo nominal Ø22 × 40 mm: permite levar energia/sinais pelo conjunto que gira. Não mede ângulo. Inventário/netlist não mostram a correspondência física das vias, quais estão ocupadas nem quais são reservas; não declarar canais livres com base apenas no modelo. |
| Tampa laranja original do LiDAR | Ø68 × 2 mm, centro nominal Blender `(0; -88; 147)` mm; já existe observador visual experimental no celular, desligado por padrão. Pode ser investigada como conferência de X/Z usando yaw manual e pan conhecidos, sem alvo novo. Identificação e precisão física ainda não comprovadas. |
| Fenda óptica / centro de medição | Centro nominal Blender `(0; -79; 147)` mm; distinto da tampa. O offset óptico atual `(0; 50; 79)` mm já entra na geometria. O bom resultado não justifica substituir esse valor por medidas antigas sem conferir a origem. |
| Base fixa / berço da bateria | Envelope nominal 138 × 88 × 50 mm; reserva de bateria 3S no modelo. Não há medição eletrônica de carga nesse berço. |
| Corpos ilustrativos de placas/antenas | Servem à apresentação da montagem. O inventário não identifica inequivocamente o centro de fase da antena DWM; não usar uma blindagem ilustrativa para recalcular seu offset. |
| Corpos de impressão e cupons de encaixe | Verificação de folga para rolamento e módulos de engrenagem antes da montagem. Não são recursos ativos que faltam ligar no programa. |

UART/DIAG do TMC permitiriam ajustes e observações adicionais caso a ligação fosse modificada. `INDEX` indica a fase elétrica da sequência de microsteps, não é um encoder do eixo nem um zero físico único. A detecção de esforço/travamento também precisa de configuração e ensaio na faixa de velocidade real. Nesta placa esses sinais não estão disponíveis ao ESP; não propomos ativá-los nesta revisão. [Datasheet oficial TMC2209, seções 13.4 e 15.4](https://www.analog.com/media/en/technical-documentation/data-sheets/tmc2209_datasheet_rev1.09.pdf).

As medidas acima vêm dos inventários existentes dos dois `.blend`, cujas cópias do projeto têm hashes iguais às originais referenciadas. Não foi feita nova extração da cena: não há Blender instalado. São dimensões nominais, não medidas do exemplar. O Blender usa Z vertical; o aplicativo usa Y vertical. Mais detalhes e limites de reconhecimento estão na [avaliação de posição automática](C:/Users/aaata/Projetos/tcc/ArScanner/docs/avaliacao-posicao-automatica-2026-10-02.md).

## Prioridade de menor impacto

1. **Conferir a seleção I²C e depois validar eixos/cadência do GY-25.** Identificar o módulo e a ponte correspondente ao manual; para o GY-25 descrito, fechar a ponte com todas as alimentações desligadas. Repetir a aquisição com a térmica, conferir aceleração/giroscópio em repouso e em movimentos conhecidos e medir intervalos/lacunas. A leitura observada com a ponte aberta não certifica esse modo. O scan sob carga fica pendente.
2. **Usar a MPU inicialmente como conferência.** Registrar movimento, gravidade, giro projetado no eixo do pan e idade/lacunas; comparar com passos e referência manual. Começar indicando coerência/divergência, sem mover o eixo ou corrigir a nuvem automaticamente.
3. **Preservar aquisição térmica parcial e repetir o cenário de uso real.** A observação atual é positiva; confirmar prévia/fusão com quadro válido e alimentação principal quando disponível. Manter a máscara e o aviso existentes.
4. **Registrar qualidade UWB e LiDAR.** Primeira etapa apenas diagnóstico, preservando parâmetros e aceitação de pontos. Comparar sessões boas e ruins antes de usar essas medidas para alterar a calibração.
5. **Explorar a tampa atual para conferir X/Z.** Usar a direção manual como entrada, validar identidade e dimensão entre vistas e apresentar diferença visual/UWB antes de qualquer correção. A MPU ajuda a conferir movimento/rotação; não fornece essa posição absoluta.

As lacunas acima de 100 ms foram observadas na coleta final. Estudar primeiro a aquisição compartilhada e o FIFO/data-ready da MPU; DMP é uma alternativa posterior. Esses recursos foram identificados como candidatos, sem implementação nesta auditoria. A integração automática só deve avançar com amostras de tempo e eixos confiáveis.

RGB simultâneo, ligações UART/DIAG/EN do TMC, novos sensores ou novos alvos são intervenções maiores. Não são necessários para preservar a nuvem que já ficou boa nem ficam autorizados por este inventário.

## Rastreabilidade da leitura

- Netlists: [scanner](C:/Users/aaata/Projetos/tcc/ArScanner/tcc.net), [base UWB](C:/Users/aaata/Projetos/tcc/ArScanner/tccvis.net), lidas com o inventariador existente.
- Firmware: [configuração do scanner](C:/Users/aaata/Projetos/tcc/ArScanner/firmware/scanner/include/config.h), [aquisição e projeção](C:/Users/aaata/Projetos/tcc/ArScanner/firmware/scanner/src/main.cpp), [driver IMU](C:/Users/aaata/Projetos/tcc/ArScanner/firmware/scanner/src/imu_mpu6050.cpp), [parser LiDAR](C:/Users/aaata/Projetos/tcc/ArScanner/firmware/scanner/include/lidar_packet_parser.h), [RGB](C:/Users/aaata/Projetos/tcc/ArScanner/firmware/scanner/src/camera_ov2640.cpp), [pan](C:/Users/aaata/Projetos/tcc/ArScanner/firmware/scanner/src/stepper_tmc2209.cpp), [ranging da base](C:/Users/aaata/Projetos/tcc/ArScanner/firmware/viewer/src/uwb_anchors.cpp).
- Modelos: inventários de montagem/impressão existentes e render local, examinados na revisão elétrica/mecânica de 02/10.
- A lista descreve o código consultado nesta auditoria e as observações identificadas por data. Mudanças posteriores devem atualizar a distinção entre implementado, instalado e fisicamente verificado.

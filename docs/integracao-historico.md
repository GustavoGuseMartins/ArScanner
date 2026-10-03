# Integração: scanner, base UWB e aplicativo

Revisão de 25/09/2026. Prioridade solicitada: **UWB e térmica**, mantendo os fios da tag nos GPIO4/5. RGB fica desativada no perfil normal. COM3 e COM4 não correspondem às placas e não foram acessadas.

## O que atualizar

- Aplicativo: `Builds/ScannerAR-altura-termica-usb.apk` (marcação por profundidade AR, cores térmicas absolutas, USB-C UWB e teste de movimento 3D).
- Scanner: projeto `firmware/scanner`, ambiente `esp32s3`.
- Base com três UWB: projeto `firmware/viewer`, ambiente `esp32dev`.

O firmware do scanner foi gravado na COM6 e o da base na COM5, ambos com verificação de gravação. O APK USB-C acima foi instalado no Samsung SM-S916B em 25/09 e abriu sem erros Android registrados; a comunicação física base→celular por USB-C ainda requer ensaio. Em novas gravações, selecione a porta real de cada placa. Não use o ambiente `esp32s3_rgb_test` nesta montagem.

Em 25/09, a base recebeu a versão que publica diagnóstico a cada ~100 ms; a gravação foi verificada pelo gravador. Com apenas a base alimentada, foram recebidos 60 estados na COM5 em nove segundos, com intervalo mediano de 110 ms e `radioMask=7`. As três âncoras ficaram em etapa 3 (sem resposta da tag), como esperado porque o scanner estava desligado. O APK USB foi recompilado após a correção de reconexão do CP210x, e o manifesto final inclui `android.hardware.usb.host`. A recepção USB real no celular e o TWR com o scanner ligado ainda precisam de ensaio.

## Comunicação efetivamente implementada

| Ligação | Transporte e dados |
| --- | --- |
| Celular ↔ scanner | Wi-Fi `ArScanner_Net`; TCP `192.168.4.1:8888`: comandos, heartbeat e pontos de 28 bytes |
| Celular → scanner | HTTP `192.168.4.1:8889`: `/status`, `/geometry`, `/scan.csv`, `/thermal`, `/rgb` |
| Base → celular, com cabo USB-C | Porta CP210x da base a 115200: diagnóstico JSON e posição `@UWB28:`; o Android solicita acesso à placa. O app prioriza leituras USB recentes |
| Base → celular, sem cabo | A base entra no mesmo Wi-Fi e transmite UDP na porta 9999: diagnóstico JSON e posição binária quando válida |
| Três âncoras ↔ tag | Rádio UWB: POLL → RESPONSE → FINAL → REPORT, sequencialmente por âncora; rede 10, canal 5, preâmbulo 10, PRF 64 MHz |

O USB-C direto usa o celular como host e o CP210x da base como dispositivo; um cabo USB-C para USB-C pode bastar se o celular fornecer energia suficiente. Ao conectar, autorize o acesso na janela Android ou toque em **Conectar base por USB-C / solicitar acesso**. A leitura USB dispensa o Wi-Fi *da base*, mas o celular ainda precisa do `ArScanner_Net` para receber os pontos LiDAR e a imagem térmica do scanner. O transporte USB não corrige erros de distância UWB nem a baixa separação física das três âncoras; ele evita perdas e atraso do trecho base→celular. A ponte serial via computador permanece como ferramenta de diagnóstico.

## Parâmetros usados e limites

| Parâmetro | Uso atual |
| --- | --- |
| Origem óptica `(0, 50, 90)` mm | Aplicada no firmware antes da rotação do pan |
| LiDAR vertical, zero para cima | Plano XY, transversal à linha eixo → LiDAR; zero matemático corrigido em 90° |
| Sentido do LiDAR | Ainda provisório: ângulo informado de 90° resulta em −X. Falta confirmar qual dos lados físicos corresponde a esse ângulo |
| Pan | 28.800 pulsos por volta, com 8 micropassos e redução 18:1; sinal −1 para o sentido físico informado. Necessita conferir uma volta real |
| Inclinação MPU | Leituras e diagnóstico ativos; compensação automática desativada até mapear os eixos da montagem |
| Orientação AR | Marcação manual da direção eixo → LiDAR, descontando o pan atual; ajustes Pitch/Yaw/Roll disponíveis |
| Altura sobre o apoio | 100 mm confirmados e configurados como padrão no aplicativo. A marcação tenta primeiro a profundidade AR da superfície apontada; se indisponível, recorre a um plano horizontal e identifica o método na tela. Somados aos 50 mm entre eixo e LiDAR, colocam o centro óptico 150 mm acima do apoio |
| Opacidade | Aplicada aos pontos existentes, inclusive com processamento pausado; material agora usa transparência |
| Térmica | Prévia HTTP de 32×24 pixels com cores em °C absolutos; 35 °C aparece vermelho por padrão. Contraste manual é opcional e altera apenas a cor, não a medição |
| RGB/temperatura na nuvem | RGB segue desativada pelo conflito GPIO4/5. A temperatura MLX é associada aos pontos LiDAR que caem no campo de visão e possuem um quadro recente; pontos sem temperatura permanecem cinza. A correspondência ainda requer conferência física com um alvo térmico conhecido |
| Pose UWB | Exige três distâncias válidas e geometria/incerteza aceitáveis; distâncias parciais continuam visíveis |

A posição de uma única tag não informa orientação. Os três UWB não substituem a calibração de inclinação e direção. A pequena separação das âncoras amplifica o erro de posição: o teste matemático da geometria configurada produz GDOP ≈ 70,87 a 2 m. Isso não é uma medição do seu hardware. O filtro mantém visíveis as distâncias mesmo quando rejeita a posição.

## Teste de bancada

1. Ligue scanner e base, conecte o celular ao `ArScanner_Net` e abra o modo de hardware. Verifique scanner ONLINE, HTTP diagnóstico v5 e diagnóstico da base chegando.
2. Com o scanner imóvel, mantenha o ajuste inicial de yaw em 200° e Pitch/Roll em zero. Aponte o centro da tela para a superfície do apoio sob o eixo e pressione **Marcar posição do scanner**. Confira **Posição marcada por**: preferir "profundidade da superfície apontada". Se aparecer "plano AR", confirme que o plano é a mesa/chão onde o scanner está apoiado, não o piso atrás dele. A altura do centro do pan acima desse apoio já inicia em 10 cm; ajuste somente se a montagem mudar.
3. Com motores parados, aponte para um ponto no apoio a pelo menos 20 cm do eixo, no sentido **eixo → LiDAR**, e pressione **Marcar direção eixo → LiDAR**. Essa direção corresponde aos 90 mm de deslocamento físico, não ao feixe de 90° do LiDAR. Y verde deve apontar para cima.
4. Inicie o scan; pause e retome. A pausa preserva a contagem do pan. **Retornar pan ao zero inicial** é um comando separado, cancelável e protegido por heartbeat. O zero é relativo à energização, sem encoder ou sensor de referência. Mover manualmente ou perder passos invalida essa referência.
5. O motor DC do LiDAR para sem prometer uma posição angular. Ao retomar, a projeção usa os ângulos dos pacotes recebidos e descarta a entrada antiga; não depende de estacionar o rotor.
6. Ajuste a opacidade de 100% para 25%: os pontos acumulados devem ficar translúcidos. Mudanças na orientação limpam a nuvem para não misturar calibrações.
7. Abra **Térmica** e use **Ampliar imagem térmica**. Verifique a taxa de quadros, o contador de pontos térmicos e os extremos medidos em °C na nuvem. A prévia e os pontos usam a mesma paleta absoluta: por volta de 35 °C vermelho; acima disso, gradualmente branco. Um objeto quente visto ao mesmo tempo pelo LiDAR e pela MLX deve colorir apenas os pontos coincidentes; os outros ficam cinza. Se a prévia mostra calor e a nuvem mostra zero pontos térmicos, investigue a fusão temporal/geométrica, não o controle de contraste. Se houver HTTP 503, a rede respondeu, mas não há quadro disponível. A indicação de RGB desativada é esperada neste perfil.
8. Ative **Unir pontos próximos no mesmo plano (LOD)** e compare paredes e cama com o controle desativado. Superfícies planas densas devem formar pequenos polígonos; bordas, vãos, detalhes próximos e limites térmicos devem continuar como pontos. O contador mostra polígonos e pontos unidos.

## Como localizar a falha UWB/térmica

- **Base sem dados:** verificar alimentação, entrada no Wi-Fi e recepção UDP 9999. Ainda não significa falha dos DWM1000.
- **Rádio ausente:** o driver operacional não leu o identificador SPI. Uma resposta apenas no teste elétrico alternativo não marca mais o rádio como pronto.
- **Tag SPI OK, polls em zero:** a tag não está recebendo POLL das âncoras. Compare com a etapa apresentada por cada âncora.
- **Polls sobem, respostas/finais/relatórios não:** os contadores e a etapa mostram até onde a troca avançou. Etapas da base: 2=TX poll, 3=resposta ausente, 4=resposta incompatível, 5=TX final, 6=timestamp divergente, 7=relatório ausente/inválido, 0=distância obtida.
- **Uma âncora ausente:** as demais continuam tentando medir; apenas as ausentes são reinicializadas periodicamente.
- **Três distâncias, posição rejeitada:** verificar geometria, atraso das antenas e incerteza. Não desativar a rejeição para fabricar uma posição estável.
- **Térmica sem ACK:** verificar alimentação e I²C SDA47/SCL48; erro −102 identifica ausência de resposta. Erro −100 indica inicialização, −101 quadro incompleto/não finito; outros códigos vêm da biblioteca.
- **ACK presente, quadros não sobem:** a conexão elétrica respondeu, mas a aquisição completa ainda falhou. A prévia não reutiliza indefinidamente uma imagem antiga como atual.

Para registrar um ensaio, salve `/status`, `/geometry` e o CSV pelo aplicativo, anotando o sentido físico do pan, a montagem e as etapas/distâncias da base. Esses dados permitem separar erro de projeção, referência AR e falha de sensor.

## Leitura real da base (24/09/2026)

A base foi identificada na COM5 como ESP32-D0WD-V3. O ensaio bit-bang leu `0xDECA0130` nos três CS; o SPI convencional leu `0xBC950360` a 2 MHz e 500 kHz nos modos 0, 1 e 2, e `0x782B07C0` no modo 3. São leituras deslocadas, não uma identificação válida do rádio. Reduzir o clock rápido do driver de 8 para 4 MHz piorou a identificação: `radioMask=0`.

O driver da base foi então configurado para clock SPI pelos GPIOs, com leitura antes de cada pulso, isolado por `ARSCANNER_VIEWER_SOFT_SPI`. Após gravar esse firmware, a base publicou de forma contínua `radioMask=7`: os três rádios passaram na identificação operacional. O estado observado foi `s1=s2=s3=3`, distâncias `-1`; a etapa 3 significa POLL transmitido e nenhuma RESPONSE recebida. O scanner não estava simultaneamente disponível para este ensaio, portanto ainda não há distância UWB confirmada. É preciso comparar com `tagPolls`, `tagResponses` e `tagStage` em `/status` do scanner com ambos alimentados.

Com scanner e base finalmente ligados, 40 amostras em 22 segundos trouxeram três distâncias válidas quase sempre (`s1=s2=s3=0`, `radioMask=7`). Logo, o enlace TWR até o REPORT funciona nos três rádios. A posição permaneceu indisponível (`state=2`) porque as distâncias não correspondiam à geometria da PCB. Em 20 segundos declarados imóveis, a aproximadamente 0,80 m, as médias foram 1,386 / 1,470 / 1,351 m, com desvio padrão de aproximadamente 0,04 m em cada rádio. O erro comum de cerca de 0,6 m e as diferenças entre rádios exigem calibração de antena/alcance antes de usar a posição. Não aplicar arbitrariamente as médias como correção: a orientação exata da PCB em relação à tag não foi medida, e um deslocamento de poucos centímetros entre âncoras provoca erro angular grande nessa base compacta.

## Validação de software

Testes de geometria, parser de comandos, retorno do pan em ambos os modos, retomada, alinhamento AR, opacidade de pontos existentes e leitura/rejeição de quadros térmicos. Validação da cena real com controles ativos e compilação dos firmwares e do APK. Houve um ensaio serial de inicialização dos rádios da base; ele não validou distância UWB, sensor térmico, motores ou ARCore no celular.

## Revisão após as fotos do teste (24/09/2026)

- A tela estava em **prévia local (scanner imóvel)**; portanto, mover o scanner fisicamente não altera a pose. A nova interface mostra junto ao seletor se chegaram posições UWB e só permite entrar no modo móvel depois de uma posição válida. A prévia local agora fixa a origem em uma âncora do ARCore no plano marcado; correções dessa âncora também reposicionam os pontos acumulados. Isso ainda requer ensaio no celular.
- Foi acrescentada a opção **Inverter vertical do LiDAR**, ativada por padrão para conferir a hipótese de chão/teto trocados. O espelhamento é feito ao redor do centro óptico a 50 mm acima do pan. Trocar a opção limpa a nuvem e exige nova varredura. O eixo Y do mundo continua apontando para cima.
- Os contadores das fotos (`tagPolls=1`, `tagResponses=1`, `tagFinals=0`, distâncias=0) mostram TWR incompleto, não posição perdida por filtragem no aplicativo. A tentativa de clock rápido a 4 MHz não resolveu a leitura da base; o driver por GPIO passou a identificar os três rádios. Ainda não há distância UWB validada.
- Na térmica, `MLX 0x33 sem ACK` e erro `-102` ocorrem antes de HTTP ou processamento de imagem. O I²C agora é iniciado uma vez a 100 kHz, e a MLX é configurada a 4 Hz para o barramento compartilhado. Se continuar sem ACK, medir 3,3 V entre J4.3 e J4.4 e conferir SDA GPIO47/J6.5 e SCL GPIO48/J6.6. A MPU também oscilou entre ACK e sem ACK nas fotos, reforçando a verificação elétrica.
- Os centros das três âncoras na PCB formam um triângulo de apenas 154 × 35,8 mm. Mesmo que o rádio passe a medir, essa geometria eleva muito a incerteza a distâncias de quarto; com o modelo provisório de 10 cm de ruído por distância, o limite de 0,5 m do solucionador rejeita a maioria das posições distantes. É necessário confirmar se a base permanecerá compacta junto ao celular ou se as âncoras poderão ser separadas no ambiente.
- Ambos os firmwares, os assemblies C# e o APK Android compilam. O serviço de licença do Unity travou na primeira tentativa; a execução com acesso ao serviço instalado gerou `Builds/ScannerAR-revisao.apk` com sucesso. O APK foi instalado no celular Samsung SM-S916B e sua presença confirmada pelo ADB. A verificação integrada da cena passou, incluindo inversão vertical e reposicionamento dos pontos após correção de âncora AR. Depois, os firmwares foram gravados no scanner e na base nas portas identificadas.
- O autor confirmou que a base ficará móvel junto ao celular, mantendo os três módulos nas posições da mesma PCB. Portanto, não há ampliação física da base de triangulação nesta versão; o limite de incerteza continuará ativo. A alimentação da câmera térmica pode ter estado incorreta no ensaio das fotos e precisa ser medida antes de tirar conclusões sobre o sensor.

## Ensaio com scanner conectado (25/09/2026)

O scanner foi identificado com segurança na COM6 como CH343, ESP32-S3 (MAC `14:c1:9f:2c:52:14`), e o firmware atualizado foi gravado com verificação de hash. O monitor serial, sem ligar os motores, reconheceu a tag DW1000 (`DECA`), mas a MLX90640 não completou a leitura de inicialização e a MPU6050 também não confirmou `WHO_AM_I` no barramento compartilhado. O ESP32 registrou falhas de leitura I²C `-1` e `263`; alguns ciclos aparentaram ACK em `0x33`, mas a leitura seguinte da memória falhou. Isso não confirma que a térmica esteja alimentada. O usuário informou ter corrigido a ligação da alimentação, mas ainda não mediu sua tensão.

Durante essas leituras, **somente o ESP estava alimentado pelo USB; a bateria da PCB não estava conectada**. Pelo netlist, a saída J4.3 alimenta MLX, DWM1000 e conector da MPU. Portanto, os erros acima não avaliam esses sensores em sua condição normal de operação. A alimentação deve ser ligada e medida antes de diagnosticar defeito na câmera, rádio ou MPU.

Com a bateria conectada posteriormente, o aplicativo passou a mostrar ACK da MPU em `0x68` e da MLX em `0x33`, segundo o usuário. O contador de quadros térmicos também passou a subir. Assim, o sensor está entregando imagens na condição normal de alimentação; a leitura anterior com USB apenas não representava a operação real. A aparência da prévia HTTP no celular ainda não foi verificada separadamente.

Na passagem de calibração solicitada pelo usuário, a base percorreu 180° ao redor do scanner e voltou duas vezes, próximo a uma parede. Foram registrados 73 conjuntos completos de distâncias em 40 segundos (`docs/uwb-rotacao-180-2026-09-24.csv`). Em 65 amostras a geometria foi impossível (`state=2`); nas oito restantes a incerteza ainda excedeu o limite (`state=3`). A diferença instantânea entre um par de distâncias chegou a 0,536 m, embora a separação física desse par de âncoras seja aproximadamente 0,085 m. Não é seguro ajustar apenas um offset fixo ou relaxar a rejeição com esse ensaio: ele mistura deslocamento, reflexos e possível obstrução por parede/corpo/celular.

Num segundo teste imóvel a aproximadamente 1,5 m e com visão direta, 37 conjuntos completos em 20 segundos (`docs/uwb-parado-150cm-2026-09-24.csv`) tiveram médias 2,233 / 2,264 / 2,250 m e desvios padrão de 0,04 m. Quatorze amostras deram geometria impossível e 23 deram posição matematicamente possível, porém a incerteza calculada destas ficou entre 7,98 e 59,61 m com o modelo conservador de 10 cm de erro por alcance. Mesmo usando só a variação estática observada (~4 cm), a amplificação geométrica permanece de vários metros. A base compacta na capinha mede alcance, mas não fornece pose 3D confiável a distâncias de quarto; o aplicativo mantém a prévia local até receber uma posição válida. O excesso de alcance observado foi ~0,6 m no teste de 80 cm e ~0,75 m neste teste de 1,5 m, valores aproximados que não autorizam aplicar um offset fixo.

Com o USB apenas, o servidor TCP do scanner iniciou e os testes de regressão do parser LiDAR e da geometria passaram. O cálculo de referência confirmou GDOP de 70,87 a 2 m para a PCB compacta (7,09 m de incerteza estimada com 10 cm de erro por distância). Esses testes não acionaram os motores nem verificaram os sensores externos sem bateria.

O retry da biblioteca MLX foi ajustado para reutilizar o dispositivo I²C e não alocar memória a cada tentativa. O erro `-103` agora indica ACK aparente seguido de falha na leitura da memória da câmera; `-102` continua indicando ausência de ACK. O firmware com esse diagnóstico também foi gravado na COM6 e seu hash verificado. A leitura serial posterior continuou mostrando falha da MLX e da MPU. Medir 3,3 V entre J4.3 e J4.4, conferir GND comum e continuidade de SDA47/SCL48 é o próximo passo elétrico.

## Fusão térmica, orientação e LOD (revisão de 24/09/2026)

O usuário confirmou que a MLX olha na direção do feixe horizontal de 90° do LiDAR e que sua lente fica aproximadamente 50 mm à direita e 25 mm abaixo do centro óptico do LiDAR, olhando nesse sentido. A projeção usa essas medidas, a origem do LiDAR `(0, 50, 90)` mm, campo térmico configurado de 110° × 75° e o espelhamento vertical padrão. Cada ponto só recebe temperatura quando está à frente da lente, dentro do campo, a pelo menos 40 cm e associado a um quadro com até 200 ms de diferença. A amostra é interpolada entre pixels da matriz 32 × 24. São parâmetros iniciais; comparar um alvo quente conhecido em posições diferentes da imagem antes de considerar a cor métrica.

O scanner tenta 8 quadros/s da MLX com I²C a 400 kHz após a inicialização. Se ocorrerem leituras consecutivas ruins, volta a 4 quadros/s e 100 kHz e reinicializa se necessário. O visor solicita a prévia a cada 200 ms, exibe os quadros/s efetivos e amplia a matriz; a resolução nativa continua 32 × 24. Um aumento da frequência sem quadros completos válidos não aumenta a informação útil.

O ajuste inicial de yaw da nuvem é 200° em torno do Y, conforme observado no teste. A direção AR ainda deve ser marcada porque o UWB da PCB compacta não mede orientação e, nas medições anteriores, não produziu pose 3D confiável. O aplicativo agora permite comparar o alcance UWB médio com a distância entre o telefone e a origem marcada pelo AR. A calibração nessa posição remove um desvio radial comum apenas para conferência; ela não determina sozinha altura, direção ou posição 3D do scanner. A origem sobre o apoio inicia em 100 mm, conforme medida do eixo do pan.

O LOD une grupos locais de pelo menos oito pontos em um polígono quando ocupam uma pequena célula, ajustam um mesmo plano dentro de 18 mm e não cruzam borda grande ou salto de profundidade. Até 0,8 m da câmera os pontos permanecem individuais; após 2,5 m a célula dobra de 16 para 32 cm. Grupos que misturam pontos com e sem térmica, ou têm variação de temperatura acima de 2,5 °C, ficam como pontos para preservar limites térmicos. A malha é reconstruída no máximo a cada dois segundos e o controle permite desligá-la para comparação. O PLY mantém os pontos originais com temperatura e cores; o LOD altera apenas a visualização.

Validação automática: teste C++ de geometria térmica e teste integrado da cena Unity, incluindo merge planar e rejeição de salto de profundidade. A confirmação no aparelho exige o novo APK e o novo firmware do scanner; a base UWB não precisa ser regravada para esta revisão.

## Teste de movimento UWB em 3D

A base já calcula a posição direta quando as três distâncias são geometricamente compatíveis e a incerteza fica abaixo de 0,5 m. Nas medições reais, essa saída quase nunca foi publicada, embora os três alcances UWB tenham chegado ao celular. O aplicativo oferece agora um **teste UWB 3D** separado: aproveita os alcances do diagnóstico mesmo quando a posição direta da base é rejeitada, sem mudar o filtro de qualidade do firmware.

Para iniciar, marque a posição do scanner no AR e pressione **Calibrar três distâncias UWB na posição marcada**. Mantenha o scanner parado por cerca de 10 segundos enquanto o visor reúne 20 conjuntos completos e calcula um desvio robusto separado para cada âncora; o celular pode fazer pequenos movimentos, pois sua pose AR entra no cálculo de cada amostra. Se a variação central exceder 20 cm, repita com visão direta. Quando o painel indicar calibração concluída, pressione **Iniciar teste UWB 3D (scanner móvel)**. O visor usa a pose AR do celular, as posições conhecidas das três âncoras na PCB e cada novo conjunto de distâncias para atualizar uma estimativa contínua de X/Y/Z; a nuvem usa a pose aceita naquele instante. Leituras ausentes, muito inconsistentes ou que exigem um salto de velocidade suspendem novos pontos até chegar outra amostra. O painel mostra posição, resíduo, incerteza geométrica e quantas atualizações foram aceitas ou rejeitadas. Se a PCB estiver girada na capinha, use **Girar referência da PCB no celular** e calibre de novo. Volte à prévia local para repetir o ensaio.

Essa trajetória é **experimental**. A calibração inicial remove um desvio naquele ponto, mas não corrige reflexos, escala de distância nem a orientação física incorreta da PCB. A regularização temporal evita saltos grandes e também pode atrasar ou subestimar movimentos, sobretudo na vertical. Três âncoras separadas por 154 × 35,8 mm, com diferenças de alcance variando cerca de 5–6 cm no ensaio imóvel a 1,5 m, não determinam uma posição 3D de alta precisão. Um resíduo pequeno mostra coerência interna dos alcances, não prova que o scanner esteja na posição real. Compare o movimento visual com uma trajetória medida antes de usar a pose para um mapa definitivo.

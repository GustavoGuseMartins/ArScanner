# AR Scanner — funcionamento, implementação e histórico do TCC

Documento central do projeto, atualizado em **04/10/2026**. Reúne o funcionamento atual, a intenção das funções, a organização do código, as dificuldades de desenvolvimento e os critérios para concluir o trabalho. Os relatórios anteriores em `docs/` são registros datados: uma configuração ou pendência descrita neles pode ter sido substituída depois.

## 1. Objetivo e escopo atual

O projeto propõe um scanner tridimensional com LiDAR e câmera térmica, conectado a um visualizador Android em realidade aumentada. A intenção é medir a geometria de um ambiente, associar temperaturas às superfícies observadas e apresentar a nuvem no mesmo referencial espacial da câmera do usuário. A proposta inicial inclui transporte por drone, localização UWB e cor RGB.

A implementação operacional está concentrada no **scanner apoiado e estacionário**. O motor gira a cabeça para formar a nuvem; o celular pode se mover para observar o ambiente e ajudar na localização. A origem e a direção do scanner precisam ser confirmadas antes da captura. O funcionamento contínuo de um scanner em deslocamento ou em voo ainda depende de validação e de uma solução de pose completa.

A base UWB mede a distância entre três rádios próximos, montados na mesma placa, e a tag do scanner. Essas medidas auxiliam a localização, mas não fornecem orientação absoluta e não garantem precisão milimétrica. A transparência no visor permite observar dados já coletados; a câmera térmica não mede através de uma parede opaca.

## 2. Estado desta revisão

| Item | Situação |
| --- | --- |
| Fontes utilizadas | Snapshot das atualizações sobre `b3697e0`, destinado à branch `codex/gy25-hud-visualizacao`. Manifesto de 247 arquivos de código/cenas/recursos/configuração do APK conferido após o build, sem diferenças; registros em `diagnostics/20261004/display-controls/refined/`. |
| Aplicativo | `Builds/ScannerAR-display-controls-20261004.apk`, 36.552.221 bytes, compilado e instalado no Samsung SM-S916B em 04/10/2026 às 23:33, preservando dados. Menu e visor passaram em Play Mode; o processo Android iniciou e permaneceu vivo, sem falhas fatais no log de inicialização. A inspeção visual no telefone ficou limitada pela tela bloqueada. `Builds/ScannerAR.apk` contém o mesmo arquivo. |
| Refinamentos desta revisão | LOD opcional desligado por padrão, tamanhos explícitos e ocultação experimental pelo lado de aquisição; todos os controles do visor no menu direito; publicação de mudanças graduais de temperatura/posição; regressões; scripts organizados com GUIDs preservados; ferramenta de build reproduzível; logs antigos arquivados. |
| Scanner e base | Após a conexão USB informada pelo usuário, o firmware atual foi compilado e gravado no ESP32-S3 pela COM6, com verificação dos blocos. A região NVS de 20.480 bytes permaneceu idêntica. A base UWB não precisou de atualização nesta etapa. |
| Validação física | O usuário fez outro ensaio e considerou a aparência adequada, relatando lentidão com LOD. A nova escolha de modos precisa de comparação medida no aparelho; precisão espacial e cadência sob carga continuam a verificar. |

O firmware gravado nesta etapa habilita compensação de inclinação, usa referências nominais de montagem de 16,2°/−1,1°, admite uma atitude de até 500 ms nesse caminho e mantém o alvo térmico após overruns. O firmware anterior documentado tinha inclinação desligada e outra política de redução de taxa. A gravação elimina essa diferença entre fonte e equipamento; a adequação física das novas escolhas ainda precisa ser demonstrada.

Os números registrados em testes anteriores são resultados das condições daqueles testes. Meta, compilação, simulação e medição física são níveis diferentes de evidência.

O APK instalado tem SHA-256 `61f4c0ab5e479fdd21f965d12f8dc754e478bde75ac53a7e8d5307e87b7ebefc`. O hash no telefone corresponde ao artefato; o certificado é o mesmo da versão substituída. A primeira instalação permaneceu em 23/09/2026 e a permissão de câmera foi preservada. Logs, APK anterior, assinaturas e validações da instalação ficam em `diagnostics/20261004/display-controls/`; build, cenas e manifesto dos fontes ficam no subdiretório `refined/`. Os registros da atualização anterior permanecem em `diagnostics/20261004/latest-phone-install/`.

O firmware atual foi preservado em `Builds/ScannerESP32S3-latest-20261004.bin`, com 883.488 bytes e SHA-256 `724f1aff6ba946120b69806568ca7a5f8f682bca965357bfce46cef9dfc20bd0`. A compilação usou 194.988 bytes de RAM e 883.125 bytes de flash. Logs, backup da região de boot/NVS/aplicação anterior, testes e verificações ficam em `diagnostics/20261004/latest-scanner-install/`.

Na conferência passiva após a gravação, a IMU estava pronta, com bias válido e `tilt=1`. Foram observados 276 quadros térmicos em 35,344 s entre diagnósticos, aproximadamente **7,81 quadros/s**, com alvo 8 e os 18 pixels excluídos preservados. Não houve aumento de erros IMU/I²C, timeouts, duplicatas ou overruns nesse intervalo. Os contadores já continham um erro IMU, um timeout e três overruns da inicialização; não são zeros absolutos. Nenhum comando de captura ou motor foi transmitido. O resultado confirma aquisição em repouso, sem demonstrar a geometria compensada ou a cadência durante varredura.

## 3. Arquitetura e caminho dos dados

```text
LiDAR + pan + GY-25/MPU6050 + MLX90640
                 |
          Scanner ESP32-S3 ---- tag DWM1000
                 |                    |
      Wi-Fi: TCP/HTTP             rádio DS-TWR
                 |                    |
                 |           Base ESP32 + 3 DWM1000
                 |                    |
                 |              USB-C / CP210x
                 v                    v
             Android: recepção + calibração UWB
                 + histórico da câmera ARCore
                 + origem/direção confirmadas
                             |
                  nuvem, térmica, LOD e registros
```

| Componente | Responsabilidade e intenção |
| --- | --- |
| Scanner ESP32-S3 | Adquirir sensores, comandar os motores, projetar os retornos LiDAR no referencial do pan e associar temperatura por geometria e tempo. |
| Base ESP32-WROOM-32 | Executar ranging com três DWM1000, informar a saúde das trocas e publicar distâncias e posição quando a solução for aceitável. |
| Android/Unity | Receber os dados, relacionar a base à câmera AR, localizar o eixo, alinhar a direção, acumular pontos e apresentar/exportar a nuvem. |
| ARCore | Fornecer pose da câmera, planos, âncoras e profundidade quando disponível. Não mede diretamente a pose do scanner. |
| Simulador | Exercitar o software com geometria, temperatura e posição sintéticas sem depender das placas. |
| Ferramentas de bancada | Coletar estados e amostras, analisar registros UWB e inspecionar modelos mecânicos/netlists. |

O scanner cria a rede `ArScanner_Net`, com senha de desenvolvimento `scanner123` e IP padrão `192.168.4.1`. Os pontos e comandos passam por TCP `8888`; estados, geometria e imagens por HTTP `8889`. A base normal transmite pela serial USB a 115200 baud. UDP `9999` permanece como opção explícita de diagnóstico/bridge, sem participação obrigatória no fluxo Android.

## 4. Organização do repositório

```text
Assets/
  Scenes/                 MenuViewer e CenaViewer; cenas antigas desabilitadas
  Scripts/
    ArScannerController.cs ligação entre os componentes e troca de modo
    GlobalData.cs          configuração transitória da sessão
    UI/                    entrada, controles e diagnóstico
    Network/               TCP, USB/UWB, relógio e simulador
    Spatial/               calibração, referências AR e estimadores de pose
    Rendering/             nuvem, cores, voxelização e LOD
  Editor/                  montagem, build e validações
  Plugins/Android/         integração USB Android
  Resources/               shader dos pontos e superfícies
  XR/ e Settings/          configuração ARCore/URP
firmware/
  scanner/                 firmware ESP32-S3 e seus testes
  viewer/                  firmware da base UWB
  common/                  matemática/protocolo compartilhados
  serial_to_udp_bridge.py  diagnóstico serial no computador
tools/                     build Android, captura e análise de bancada/CAD
docs/                      evidências e relatórios históricos
Packages/                  dependências Unity
ProjectSettings/           configurações versionadas do projeto
Builds/                    APKs/binários locais, ignorados pelo Git
diagnostics/               logs, capturas e resultados locais
```

`MenuViewer.cs` foi movido para `Assets/Scripts/UI/` e `GlobalData.cs` para `Assets/Scripts/`, junto dos respectivos `.meta`. A identidade dos scripts nas cenas foi preservada. Os 43 logs antigos da raiz foram arquivados em `diagnostics/historical-root-logs/`. Não se deve versionar `Library/`, `Temp/`, caches de IDE ou resultados da PlatformIO como se fossem fontes.

As bibliotecas DW1000 em `firmware/*/lib/` contêm adaptações locais. Não substituir essas cópias por uma versão genérica sem conferir o suporte à seleção de rádio e ao SPI da base. Modelos `.blend`, netlists e planos antigos foram preservados como material do desenvolvimento.

## 5. Como operar

1. Ligue e apoie o scanner, conecte o Android à rede do scanner e, se usar UWB, conecte a base ao celular por USB-C. Autorize câmera e USB quando solicitado.
2. O menu verifica TCP e USB separadamente. **Abrir Scanner AR** exige o scanner disponível. A ausência de UWB permite usar a marcação manual; abrir o visor não liga os motores.
3. Confira o **zero físico do pan**. Quando não houver referência válida, alinhe a cabeça à frente física escolhida e confirme o zero. Essa confirmação é do operador, pois o sistema não tem encoder/homing independente.
4. Fixe o eixo no apoio: mire a superfície diretamente abaixo do eixo para marcação manual, ou use a estimativa UWB multivista e confirme **Fixar eixo UWB estimado**. A altura do eixo acima do apoio deve corresponder à montagem.
5. Defina a direção olhando um ponto livre no mesmo apoio, na direção da frente do LiDAR. A interface sugere cerca de 30–50 cm; o código aceita distância horizontal de 25 cm a 1,5 m e confere a altura do plano.
6. Use **Iniciar / retomar captura**. O aplicativo verifica referências, estado recente, AR e comandos pendentes, e só apresenta captura ativa após confirmação do scanner.
7. Observe geometria e cores, abra a imagem térmica quando necessário e use pausa, limpeza ou exportação. Uma nova posição/direção ou ajuste geométrico limpa a nuvem para evitar misturar referenciais.
8. Ao voltar ao menu, o aplicativo solicita a parada. Desconexão ou falta de heartbeat também interrompem os motores no firmware.

Na localização automática, mova o celular para obter vistas diferentes e pause durante as medidas. Três distâncias tomadas de uma única pose não resolvem a incerteza da placa compacta. Uma estimativa provisória serve ao diagnóstico; a captura exige consenso, apoio/altura, fixação explícita e direção alinhada.

### 5.1 Calibração UWB

O menu permite calibrar em duas distâncias físicas conhecidas entre o centro da placa e a tag. São coletados 20 ciclos novos em cada posição. Uma média aparada elimina extremos; variação excessiva impede salvar um perfil instável. A correção por rádio é `distância_corrigida = escala × distância_bruta + offset`, considerando a geometria dos rádios.

O perfil v2 valida distâncias de 0,3–10 m, separação física mínima de 30 cm, crescimento bruto mínimo de 20 cm, escala de 0,5–1,5 e offset de até ±2 m. Perfis antigos/provisórios não autorizam automaticamente a localização. Um bom ajuste em dois pontos ainda precisa ser verificado em outras distâncias e orientações.

Na inicialização, UWB automático requer perfil v2 válido e transporte USB. Sem essas condições, o gerenciador entra na marcação manual pelo AR.

### 5.2 GY-25 e referências distintas

O zero persistente do pan, a direção do scanner no AR e a referência da GY-25 têm papéis diferentes. O primeiro relaciona passos e montagem; a direção AR relaciona o scanner ao ambiente; a GY-25 acompanha variação angular relativa à direção inicial.

Com o conjunto parado, **Referenciar GY-25** coleta aproximadamente três segundos de repouso e estima o resíduo do giroscópio. Depois é necessário ativar **Acompanhar giro com GY-25**; apenas referenciar não ativa yaw. Leitura, bias, gravidade, idade, quaternion e referência precisam ser válidos. Uma MPU6050 sem magnetômetro não fornece heading absoluto. O uso de yaw continua uma opção a validar no equipamento.

A inclinação aplicada pelo firmware recente é uma configuração independente desse botão de yaw. O HUD informa a origem angular e os estados para evitar confundir leitura do sensor com aplicação efetiva na nuvem.

## 6. Funções da interface e sua intenção

| Função | Comportamento e finalidade |
| --- | --- |
| Verificar/reconectar scanner / autorizar USB novamente | Mostrar disponibilidade de rede e base; recuperar conexão e permissão USB sem assumir que Wi-Fi implica UWB. |
| Capturar/salvar/remover perfil UWB | Corrigir diferenças sistemáticas por rádio com medidas conhecidas; manter rastreabilidade da calibração. |
| Confirmar zero / retornar pan ao zero | Estabelecer a referência manual física e retornar pelo menor caminho de passos. |
| Marcar/corrigir eixo no apoio | Definir a origem estacionária no mundo AR ou refazer uma localização incorreta. |
| Fixar eixo UWB estimado | Aceitar explicitamente um candidato automático após os critérios de estabilidade. |
| Alinhar direção no apoio | Relacionar a frente física do scanner ao yaw do mundo AR. |
| Iniciar, cancelar início, pausar e retomar | Controlar a aquisição com confirmação de estado; uma intenção de comando não equivale a motor ativo. |
| Velocidade do pan | Ajustar 0,5–10 RPM na interface quando parado; a API do motor admite 0,1–10 RPM. |
| Prévia térmica / RGB de diagnóstico | Conferir a imagem que participa da associação; RGB depende de perfil de hardware e não está ativa normalmente. |
| Cores térmicas, escala fixa e contraste | Alternar apresentação de geometria/temperatura; a escala absoluta usa azul abaixo de 20 °C, transições até vermelho a 35 °C e branco a 45 °C. |
| Opacidade / indicador de eixos | Ajustar leitura visual da nuvem e referência espacial sem alterar os dados medidos. |
| LOD ligado / somente pontos | Iniciar com LOD desligado. Ativar superfícies quando desejado; desligar remove as superfícies e interrompe novos cálculos de geometria, conservando a nuvem. |
| Tamanho ×1/4, ×1/2, ×1, ×2, ×3, ×4 | Escolher diâmetros de 3,125; 6,25; 12,5; 25; 37,5; 50 mm. A escolha desativa o tamanho adaptativo e atualiza pontos já coletados, inclusive pausados. |
| Tamanho adaptativo com LOD | Permitir expansão visual dos pontos restantes pela distância e suporte medido quando o LOD está ativo; o botão de tamanho volta ao diâmetro fixo escolhido. Em somente pontos o diâmetro é fixo. |
| Ocultar pontos vistos por trás (experimental) | Opção desligada por padrão. Usa o lado observado na aquisição como aproximação; conserva dados e exportação, mas pode ocultar pontos de uma superfície cujo raio não representa sua normal real. |
| Limpar nuvem / exportar PLY | Reiniciar observações ou salvar a nuvem consolidada com posição, cor, temperatura e flags. |
| Ignorar retornos LiDAR fracos | Rejeição opcional pelo aviso do sensor, desligada por padrão; mudar a regra limpa a nuvem. Não introduz corte de distância no visor. |
| Referenciar / acompanhar giro GY-25 | Coletar repouso e habilitar, separadamente, yaw relativo com critérios de validade. |
| Atualizar geometria / salvar amostras CSV | Consultar parâmetros efetivos e guardar retornos locais anteriores ao alinhamento AR. |
| Diagnóstico da sessão | Exibir FPS, pacotes, descartes, USB, ranges, estado espacial e saúde dos sensores; guardar registros para análise. |
| Lado/espelhamento térmico | Ajustar o perfil óptico entre quatro alternativas; persistir no scanner e limpar a nuvem. |
| Altura do eixo / girar placa USB 90° | Relacionar apoio, rádio e câmera à montagem física; a rotação da placa é persistida. |
| Offsets yaw/pitch/roll e inversão vertical | Ensaiar a montagem; mudanças geométricas invalidam observações que pertencem ao referencial anterior. |
| 2D/3D, 180°/360° e PWM LiDAR | Ensaios de bancada com pan desligado/ligado, modo de giro e motor LiDAR; controles condicionados ao estado. |
| Reiniciar UWB / deslocamento livre | Refazer localização ou observar o estimador experimental; deslocamento livre não libera captura física. |
| Reconhecimento da tampa laranja | Experimento multivista de forma/cor da tampa original; desligado por padrão. |
| Testar direção automática por ponto no apoio | Ensaio guiado que usa um ponto escolhido pelo operador após pausa; não reconhece sozinho a frente física do scanner. |
| Simulação no Editor | Exercitar controles, geometria e térmica sem placas; não demonstra precisão ou temporização física. |
| Recolher/mostrar controles e voltar ao menu | Liberar área de visualização e encerrar o visor com pedido de parada. |

O aplicativo exibe a taxa térmica medida e o alvo. A API atual solicita 8 quadros/s, mas não há seletor de FPS térmico na HUD. Pedidos de taxa/perfil/referência precisam ser confirmados consultando o estado após a resposta HTTP.

No visor, todas as ações ficam no menu da direita, com uma rolagem compartilhada para visualização, conexões, captura, localização, sensores, montagem e experimentos. O painel esquerdo mostra somente estados. A prévia térmica central é fechada pelo menu direito, inclusive após desconexão. O menu usa a altura da área segura do telefone e pode ser recolhido.

Na configuração atual, a cor térmica exige também a inversão vertical compatível com a fusão do firmware. Desligar essa inversão deixa a geometria cinza. A cor exportada no PLY segue a configuração de apresentação vigente, não representa automaticamente RGB bruto de uma câmera.

## 7. Coordenadas, tempo e validade

### 7.1 Geometria

A convenção é X para a direita, Y para cima e Z para a frente; pan horário positivo visto de cima. O firmware trabalha em metros e transmite XYZ em milímetros. O aplicativo converte para metros e coloca os pontos no mundo AR a partir do eixo confirmado.

O caminho geométrico é: raio LiDAR → centro óptico → rotação da cabeça no instante medido → eixo do pan → direção/origem confirmadas no AR. A atitude e a rotação da câmera do celular não são a atitude do scanner. Pitch/roll transmitidos acompanham uma geometria já projetada no firmware e não devem ser aplicados novamente sem conferir o contrato.

Os fontes atuais usam origem LiDAR `(0, 0,050, 0,079)` m, yaw de montagem 90°, zero angular 90°, relação nominal de 28.800 pulsos por volta de pan e sinal de pan −1. A distância de 79 mm vem do modelo; uma medida histórica de 90 mm aparece em registros anteriores. É preciso comparar essas referências à montagem real, sem tratá-las como a mesma calibração. A geometria efetiva deve ser consultada em `/geometry`.

O centro da tag gira junto com a cabeça. Sua posição precisa ser convertida para o eixo usando o braço de montagem e o yaw/pan correspondente. A localização UWB identifica a tag; o renderizador precisa da origem do pan.

### 7.2 Tempo e localização UWB

Scanner, base e celular têm relógios distintos. `UwbDeviceClockMapper` estima a relação do relógio da base com o celular pelo menor atraso observado; isso não mede a latência absoluta. O histórico AR interpola a pose da câmera para cada instante de ranging e rejeita lacunas ou mudanças grandes de época.

Na versão 2 da base, cada rádio tem timestamp próprio. O aplicativo verifica ciclo temporal, aquecimento do relógio e poses correspondentes. Sem essas informações não substitui silenciosamente a pose temporal por uma pose posterior. Reinícios, wrap do relógio, medidas duplicadas e perda de tracking são tratados nas validações.

A PCB de três âncoras coplanares tem ambiguidade de espelho e amplifica erros a distância. O estimador multivista busca variedade de observações com baseline de pelo menos 45 cm; o telefone precisa pausar. O consenso de posição exige ciclos novos, dispersão e duração aceitáveis. Fixação estável significa consistência interna, não precisão física comprovada.

### 7.3 Associação térmica

A MLX90640 tem matriz nativa de 32×24 pixels. A imagem é exibida girada 90° para a direita, com 24×32 pixels. O perfil da cabeça usa aproximadamente 75° horizontal e 110° vertical; esse campo é menor que a fatia completa medida pelo LiDAR.

A associação transforma o ponto para a cabeça no instante do quadro térmico, preservando o deslocamento entre as lentes. Exige pose nos dois instantes, validade do pixel e diferença temporal de até ±200 ms. O limite de 0,40 m trata paralaxe na associação térmica; não apaga a geometria LiDAR.

Um quadro completo exige duas subpáginas coerentes. A EEPROM define pixels elegíveis; a unidade documentada possui 750 elegíveis e 18 excluídos. A máscara é transmitida e apresentada sem inventar temperatura nos pixels excluídos. Campo de visão, paralaxe, subpáginas e ausência de profundidade térmica limitam a associação: um objeto quente pode aparecer na imagem sem ser a superfície interceptada pelo LiDAR.

## 8. Nuvem de pontos e LOD

`ThermalPointCloudRenderer` mantém até 80 mil pontos por padrão, com voxelização de 25 mm e quads iniciais de 12,5 mm. Retornos independentes não têm identidade física persistente; a fusão só combina amostras do mesmo voxel do mundo. Há buffer circular quando a capacidade acaba.

Uma leitura sem térmica não apaga uma temperatura válida já observada no voxel. Temperaturas repetidas usam EMA com peso 0,25. A posição é refinada por média limitada a 32 observações e deadband de 15 mm. Na revisão atual, a publicação compara com o último estado exibido: mudanças acumuladas acima de 0,3 °C ou 2 mm passam a atualizar cor/posição e verificar o LOD. Antes, comparar somente o incremento anterior podia congelar a apresentação durante aquecimento ou deslocamento gradual.

`SurfaceLodBuilder` calcula planos/retângulos com suporte local, em uma tarefa de segundo plano. Objetos Unity, meshes e partículas são publicados na thread principal. Buracos, linhas isoladas, diferença de profundidade e fronteiras térmicas impedem preencher áreas sem suporte. O LOD simplifica apresentação; a exportação mantém observações consolidadas.

O padrão atual é **somente pontos**. `SetSurfaceLodEnabled(false)` remove a malha, repõe os pontos existentes e invalida a publicação de jobs anteriores. Não agenda `BuildGeometryData`, mesmo com tamanho adaptativo ligado. Um cálculo que já começou pode terminar em segundo plano, mas seu resultado é descartado; a troca não apaga medições. Ligar novamente permite reconstruir superfícies usando o conjunto atual.

`SetPointSizeMultiplier` usa 12,5 mm como referência, aceita fatores de 0,25 a 4 e desliga a adaptação para apresentar o diâmetro fixo exato. O ajuste muda apenas partículas visíveis, inclusive durante pausa; voxelização, contagem, temperatura, posições e PLY conservam os dados. Superfícies LOD mantêm dimensões obtidas do suporte medido.

Superfícies compatíveis são conservadas durante novos retornos. Uma observação incompatível retira apenas os retângulos afetados. Resultados atrasados conferem geração, configurações e identidade dos slots; limpeza, rebase ou reutilização do buffer não podem restaurar uma nuvem antiga. Um hotspot pode retirar um retângulo grande inteiro, ainda uma limitação conservadora da divisão utilizada.

Quads individuais são visíveis pelos dois lados por padrão, inclusive ao caminhar paralelamente à parede. A opção experimental `SetHideBackFacingPoints` compara a direção ponto→câmera com a direção ponto→scanner da aquisição: produto escalar normalizado abaixo de −0,05 oculta a partícula. Direção desconhecida ou câmera coincidente conserva visibilidade. Essa aproximação não estima normais reais nem resolve oclusão; acompanhar o movimento do celular não reconstrói a geometria. Superfícies ajustadas usam seu próprio material com profundidade/backface, independente dessa opção. Não há limite artificial de 0,15–20 m no visor: coordenadas nulas/não finitas são rejeitadas, e a validade de distância do sensor permanece no driver.

## 9. Firmware e concorrência

| Função | Implementação e intenção |
| --- | --- |
| LiDAR | Parser UART de pacotes de 22 bytes, checksum, quatro amostras, velocidade e intensidade/aviso de sinal. |
| Pan/TMC2209 | Pulsos por timer/ISR, rampa de velocidade, giro 360°/ping-pong 180°, hold, retorno e histórico temporal. Sem encoder independente. |
| GY-25/MPU6050 | Leitura própria por I²C, bias, quaternion/yaw relativo, referência parada, histórico temporal e diagnóstico persistente da última invalidação. |
| MLX90640 | EEPROM, máscara, subpáginas, cálculo térmico, snapshot coerente e associação espacial/temporal. |
| Tag DWM1000 | Responder DS-TWR com baixa latência; não calcula posição global ou direção AR. |
| Controle/rede | Filas de comandos e pontos, reconexão, heartbeat, confirmação de estados e encerramento de escrita parcial. |
| Base UWB | Interrogar três rádios sequencialmente, invalidar ciclo incompleto, publicar ranges/estágios e trilateração quando aceita. |

No scanner, sensores/pan ficam no core 1 com prioridade 2. A tarefa auxiliar MLX/MPU tem prioridade 3 e é dona única do `Wire`, atendendo IMU entre transações drenadas. A prioridade maior busca copiar RAM antes da próxima subpágina, sem substituir a temporização de hardware dos pulsos. Rede e HTTP ficam no core 0, prioridade 1; UWB usa prioridade 2. RGB só cria sua tarefa quando o perfil permite inicialização.

O I²C inicia a 100 kHz e usa 400 kHz para a térmica. As leituras de RAM são divididas em blocos de 128 bytes; EEPROM em 64 bytes. Há prazos, serviço cooperativo da IMU e descarte de quadro incoerente. As fontes atuais conservam o alvo de 8 quadros/s após overruns; fallback por falha de transporte ainda pode diminuir o desempenho. Alvo configurado e taxa real precisam ser observados separadamente.

O zero do pan é salvo com checkpoint/estado de movimento. O firmware marca movimento antes de emitir pulsos, e não inicia se não conseguir preservar a referência coerente. Mover manualmente a cabeça ou perder passos exige nova conferência física.

Na base, os centros nominais formam triângulo de base 175 mm e lados 100 mm. DS-TWR usa quatro mensagens por rádio, timestamps locais de 40 bits e report de distância. O cálculo demonstrativo aceita um lado do plano e aplica rejeição por incerteza; ranges continuam disponíveis mesmo quando XYZ é recusado. O filtro é uma suavização escalar, sem EKF ou solução de yaw.

## 10. Contratos de comunicação

### 10.1 Pontos TCP

Cada ponto tem **28 bytes, little-endian**, sem cabeçalho de lote. Uma escrita pode conter até 15 registros, e o receptor precisa reconstruir registros fragmentados pelo TCP.

| Offset | Tipo | Conteúdo |
| --- | --- | --- |
| 0, 4, 8 | `float32` | X, Y, Z em milímetros. |
| 12 | `float32` | Temperatura em °C; a flag de indisponibilidade prevalece sobre qualquer placeholder. |
| 16, 17, 18 | `uint8` | R, G, B; branco no perfil atual não significa cor RGB capturada. |
| 19 | `uint8` | Flags: `0x01` planar reservado, `0x02` hotspot, `0x04` térmica indisponível, `0x08` sinal LiDAR fraco. |
| 20, 22 | `int16` | Pitch e roll em centigraus. |
| 24 | `uint32` | Timestamp local do scanner em milissegundos. |

| Comando | Payload | Intenção |
| --- | --- | --- |
| `0x01` / `0x02` / `0x03` | Nenhum | Iniciar / parar / heartbeat. |
| `0x04` | `float32` | RPM do pan. |
| `0x05` | Byte 0/1 | 360° / ping-pong 180°. |
| `0x06` | Byte 0–255 | PWM do motor LiDAR. |
| `0x07` | Byte 0/1 | Pan parado/girando; LiDAR pode continuar medindo. |
| `0x08` / `0x09` | Nenhum | Retornar ao zero / confirmar zero físico. |

O heartbeat é enviado aproximadamente a cada segundo durante operações relevantes. Ausência por mais de 2,5 s ou desconexão interrompe motores. A confirmação de comando descarta snapshots de estado anteriores ao pedido, evitando tratar um estado velho como resposta nova.

### 10.2 HTTP

| Endpoint | Método | Resultado/intenção |
| --- | --- | --- |
| `/status` | GET | Diagnóstico v14 de pan, IMU, LiDAR, UWB, térmica, captura e contadores. |
| `/geometry` | GET | Constantes, offsets, sinais e perfil efetivo para interpretar pontos. |
| `/scan.csv` | GET | Snapshot circular dos últimos 1.024 retornos, tempos, ângulos, intensidade e projeção antes do AR. |
| `/rgb` | GET | JPEG ou indisponibilidade com motivo. |
| `/thermal` | GET | 776 bytes: min/max `float32` e 768 intensidades; compatibilidade com quadros elegíveis. |
| `/thermal/masked` | GET | 872 bytes: payload anterior e 96 bytes de máscara de elegibilidade. |
| `/thermal/orientation?profile=0..3` | POST | Lado/espelhamento persistente, com scanner parado. |
| `/thermal/frame-rate?fps=8` | POST | Pedido assíncrono de alvo/recuperação; conferir `/status` depois. |
| `/imu/orientation/reference` | POST | Pedido de referência em repouso. |
| `/imu/orientation/mode?enabled=0\|1` | POST | Desligar/habilitar acompanhamento após validar a referência. |

HTTP `202` indica pedido enfileirado; `409` indica incompatibilidade com o estado e `503`, indisponibilidade. O aplicativo serializa HTTP e prioriza controles/estado. A resposta térmica não contém um timestamp no próprio payload; saúde e idade são consultadas no estado.

### 10.3 USB/UWB

A integração Android usa `ArScannerUsbSerial.jar`, com fonte em `tools/android/UwbUsbSerial.java`, para CP210x `VID 10c4 / PID ea60`, 115200, 8N1 e leitura de linhas. Não é um driver genérico de todos os conversores USB. DTR/RTS ficam desligados para evitar resets involuntários.

Uma posição usa linha `@UWB28:` e 56 caracteres hexadecimais: seis `float32` para XYZ da tag e três distâncias em metros, seguidos de `uint32` timestamp; são 28 bytes de outro contrato, diferente do ponto TCP. Diagnóstico JSON tem `kind=uwb_status`, versão, `radioMask`, `state`, estágios por rádio, distâncias e incerteza. A versão 2 acrescenta validade do ciclo e tempos individuais.

O aplicativo exige números finitos, rádios válidos e ciclos recentes/coerentes. Não completa um ciclo novo com distâncias antigas. A bridge Python converte essas linhas para UDP no computador; logs não enquadrados são ignorados.

TCP UWB em `192.168.4.2:9999` também existe como transporte configurável para diagnóstico. Nenhum desses modos de rede é fallback invisível da conexão USB normal.

## 11. Persistência, registros e exportações

| Dado | Local e duração |
| --- | --- |
| Perfil de alcance UWB v2 | `PlayerPrefs`, chaves `arscanner.uwb.range.v2.*`; escalas/offsets por rádio e marcador de validade. |
| Rotação da placa USB | `PlayerPrefs`, `arscanner.uwb.boardRotationZ`. |
| Zero/checkpoint pan | NVS do scanner, namespace `ars_pan`. |
| Perfil de lado/espelhamento térmico | NVS do scanner, namespace `arscanner`. |
| IP, porta, seleção de modo | `GlobalData`; configuração transitória da sessão. |
| Origem/direção AR e referência GY-25 | Referências da sessão; não equivalem ao zero físico salvo. |
| Nuvem final | `Application.persistentDataPath/Scans/Scan3D_*.ply`, ASCII, XYZ em metros, cores, temperatura e flags; ausências térmicas como `nan`. |
| Amostras antes do AR | `Scans/ScanRaw_*.csv`; snapshot do scanner, separado da nuvem consolidada. |
| Estado do scanner | `ScannerDiagnostics/Scanner_*.jsonl`. |
| Ranges/decisões espaciais | `UwbDiagnostics/Uwb_*.csv`, com brutos/corrigidos, tempos, poses, gates, épocas e decisões. |
| Eventos de calibração | `UwbCalibrationEvents/events.csv`. |

Os nomes dos logs de sessão usam UTC; vídeos e horários apresentados ao usuário podem usar America/Sao_Paulo. Toda análise deve declarar a conversão utilizada. Um snapshot circular ou polling não constitui registro sem perdas de todos os pontos/eventos. Contadores de associação do firmware, pontos recebidos e voxels do aplicativo também não são grandezas equivalentes.

## 12. Mapa de funções do código

As tabelas cobrem funções operacionais e APIs principais. Callbacks de ciclo Unity (`Awake`, `Start`, `Update`, `OnGUI`, `OnDestroy`) ligam essas operações à inicialização, atualização, desenho e encerramento.

| Classe/módulo | Funções e intenção |
| --- | --- |
| `ArScannerController` | `Awake` conecta as referências; `SetSimulationMode` troca/reinicia a cadeia inteira entre hardware e simulação. |
| `GlobalData` | Campos estáticos de destino/rede e seleção de sessão; não possui método próprio. |
| `MenuViewer` | `IniciarComHardware`, `IniciarModoSimulacao`, `CarregarCenaDoViewer`: selecionar modo/abrir cena; helpers sondam rede, criam layout, recebem USB e calibram ranges. |
| `ArScannerHUD` | `CalculateLayout`, `ShowFeedback` e grupos `Draw*`: organizar área segura, estados, controles, diagnóstico, montagem e experimentos. |
| `PointCloudTcpReceiver` | `ConnectToScanner`, `Disconnect`, `TryParsePointPacket`, `EnqueuePoint`: receber/validar registros. `SendStartScan`, `SendStopScan`, `SendParkPan`, `ConfirmPanZeroReference`, `SendSetSpeed`, `SendSetMode`, `SendPanEnabled`, `SendSetLidarSpeed`, `SendCommand`: controlar o scanner. |
| HTTP/IMU no receiver | `RequestGeometry`, `DownloadScanCsv`, `RequestImuOrientationReference`, `RequestImuOrientationMode`, `RequestThermalFrameRate`, `RequestThermalOrientation`, `SetCameraPreview`: consultar/configurar sensores; validadores `IsUsable*`/`IsValid*` e `TryCalculateThermalFrameRate` evitam usar estado obsoleto ou fabricar taxa. |
| `UwbDataReceiver` | `StartReceiver`, `StopReceiver`, `RequestUsbPermissionAgain`, `TryParseSerialPacket`, `TryGetLatestRangeTimes`, `IsCoherentRangeCycle`, `StageName`, `SetSimulatedPosition`: ciclo de transporte, validação, tempos e diagnóstico. |
| `UwbDeviceClockMapper` | `Clear`, `TryMap`: manter conversão estimada de relógios com reinício/rollover. |
| `PointCloudSimulator` | `ResetSimulation` e geração de raios/sala/alvo quente: criar dados sintéticos pela mesma cadeia de apresentação. |
| `UwbAnchorManager` | `InitializeReferences`, `PlacePreviewAtScreenCenter`, `AlignPreviewForwardAtScreenCenter`, `TryConfirmPoseCandidate`: montar, marcar, alinhar e fixar pose. `ResumeAutomaticUwb`, `SetScannerStationary`, `InvalidateHeading`, `RotateBoardInPhone90`: trocar estado e invalidar referências incompatíveis. |
| Geometria/gates do manager | `AutomaticCaptureReady`, `IsReliableAutomaticPose`, `IsApproximateAutomaticPose`, `IsDepthCompatibleWithPreviewSupport`, `TryGetDiagnosticAnchors`: classificar qualidade/apoio. `TryCalculateScannerYaw`, `RootYawForAlignedHeading`, `TryCalculateScannerYawFromTag`, `PanOriginFromRotatingTag`, `TryUseVisualPoseObservation`, `UpdateDroneAttitude`: converter e validar referenciais. |
| `ArCameraPoseHistory` | `Clear`, `TryGetBounds`, `Add`, `TryGet`: guardar/interpolar pose sem extrapolação e separar épocas. |
| `UwbRangeCalibrationProfile` | `HasSavedCalibration`, `Load`, `TryFitTwoPoint`, `TrySaveTwoPoint`, `Reset`: validar, calcular e persistir correções. |
| `UwbPhonePauseGate` | `Clear`, `Update`: identificar repouso do telefone. |
| `UwbArMultiviewEstimator` | `Clear`, `AddSample`, `TryEstimate`, `TryEstimateAtHeight`: estimar tag por ranges e poses de vistas diferentes. |
| `UwbInstantPoseEstimator` | `Clear`, `TryUpdate`: solução instantânea com correção, espelho, prior e critérios de qualidade. |
| `UwbMotionEstimator` | `TryRobustBias`, `TryEstimate`, `GeometrySigma`: estatística/ajuste experimental e incerteza geométrica. |
| `StationaryPoseConfirmation` | `Clear`, `TryAdd`, `TryAddValidatedMultiviewCycle`, `RangeCycleMatchesCandidate`, `RequiresHeadingRevalidation`, `MatchesSupportHeight`: consenso, validação com ciclos novos e revalidação. |
| `UwbVisualPositionRefiner` | `Clear`, `Add`, `TryRefine`: ajustar horizontalmente posição por profundidade repetida e vistas independentes. |
| `ScannerVisualPoseObserver` | `ResetObservations`, `SetNaturalCoverRecognition`, `TryGetObservation`, `FrameTimestampMatches`: capturar/validar observações da tampa na mesma época e quadro. |
| `ScannerDiskPoseEstimator` / detector | `Clear`, `Add`, `TryEstimate`, `ShapeCost`, `TryPredictMoments`, `WorldToPixels`, `ViewportToImage`, `ImageToViewport`, `Find`: comparar disco projetado e regiões laranja multivista. |
| `ThermalPointCloudRenderer` | Recepção/fusão por voxel, `ClearPointCloud`, `RebaseWorldPoints`, `ExportToPLY`, `SetRejectWeakLidarReturns`, `SetSurfaceLodEnabled`, `SetPointSizeMultiplier`, `PointSizeMultiplier`, `SetHideBackFacingPoints`, `AdaptivePointDiameter`, `ThermalPalette`, `AbsoluteThermalPalette` e publicação regional de quads/LOD. |
| `SurfaceLodBuilder` | `Build`, `BuildGeometryData`, `CreateMesh`: calcular suporte/retângulos como dados gerenciados e criar o mesh na thread principal. |
| `EditorCameraController` | Navegação de desenvolvimento; não está referenciado nas cenas atuais. O uso futuro exige conferir compatibilidade com Input System. |
| Firmware scanner | Drivers `begin/update`, parser LiDAR, `setSpeedRpm`, `park`, `confirmPhysicalZero`, referência/modo IMU, histórico yaw e aquisição térmica; tarefas possuem sensores/rede e aplicam comandos. |
| Firmware base | `begin`, `retryMissing`, `readDistances`, `calculatePosition`, `reset`, `publishStatus`: descobrir rádios, ranging coerente, trilateração e serial. |
| Bridge | `decode_line`, `LineDecoder.feed`: validar/enquadrar serial antes de enviar UDP. |
| Editor/build | `AndroidBuildScript.BuildAndroidApk`, `SceneSetupHelper` e validadores: fixar cenas, montar componentes intencionalmente, verificar e compilar. |

## 13. Compilar, instalar e testar

O projeto registra Unity **6000.5.4f1**, URP **17.5.0**, AR Foundation/ARCore **6.5.0**, Input System **1.19.0** e Test Framework **1.7.0**. Android usa IL2CPP, ARM64 e mínimo API 26. O dispositivo precisa de ARCore compatível. Instale o editor correspondente com Android Build Support, SDK, NDK e OpenJDK pelo Unity Hub.

Pela interface Unity, **ArScanner > Build Android APK** gera `Builds/ScannerAR.apk`, incluindo `MenuViewer` e `CenaViewer`, nessa ordem. O menu de atualização das Build Settings aplica as mesmas cenas. A injeção de componentes é ferramenta de montagem intencional, não uma etapa necessária em toda compilação.

Pela raiz do projeto no PowerShell:

```powershell
.\tools\build_android.ps1 -CheckScenes
```

O script encontra a versão registrada no Unity Hub, verifica as duas cenas em Play Mode, executa as validações do projeto e gera o APK. `-OutputPath`, `-LogDirectory` e `-UnityPath` permitem destinos/editor explícitos. O JSON de resultado guarda versão do editor, tamanho e SHA-256. O script compila; a instalação é uma etapa separada.

Para instalar no telefone conectado, use o ADB do SDK Unity, escolha o serial exibido em `devices -l` e substitua o aplicativo preservando dados:

```powershell
$adbPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adbPath devices -l
& $adbPath -s SERIAL_DO_CELULAR install -r .\Builds\ScannerAR.apk
```

Antes de atualizar, preserve o APK instalado e confira assinatura compatível; depois verifique hash instalado e inicialização. O identificador atual é `com.UnityTechnologies.com.unity.template.urpblank`, versão `0.1.0`, código 1. A identidade final de publicação e o versionamento precisam ser definidos com migração dos dados e assinatura planejadas; trocar o package agora criaria outro aplicativo.

Firmware, a partir da raiz:

```powershell
pio run --project-dir firmware/scanner --environment esp32s3
pio run --project-dir firmware/viewer --environment esp32dev
```

Para gravação intencional, acrescente `--target upload --upload-port PORTA_IDENTIFICADA` ao comando correspondente. A porta deve ser identificada no equipamento atual. O ambiente `esp32s3_rgb_test` é ensaio separado com tag desligada; GPIO4/5 da câmera conflitam com o SPI UWB, impedindo considerar RGB+UWB simultâneos prontos nesse perfil.

### 13.1 Validação de software

`ScannerPoseValidation.BuildValidated` executa LOD, pose temporal, UWB instantâneo/multivista, consenso estacionário, controles, máscara térmica, qualidade LiDAR e pose visual antes do build. `MenuSceneValidation.CheckMenuInPlayMode` e `ViewerSceneValidation.CheckViewerInPlayMode` verificam as cenas reais e sua inicialização. Os testes de LOD incluem hotspots, buracos, paredes independentes, buffer circular, jobs atrasados, limpeza/rebase, configurações, mudanças graduais/ruído e os novos controles: ausência de jobs em somente pontos mesmo com adaptação ligada, descarte de resultados após desligar/religar, tamanhos exatos com captura pausada, contagem nativa de partículas e ocultação opcional com direção desconhecida/câmera coincidente. O visor exercita os grupos expandidos no mesmo scroll durante quadros reais de GUI; layout verifica margem direita, altura disponível e ausência de sobreposição em retrato/paisagem/área segura.

O firmware possui testes nativos de matemática espacial, referência pan, parser LiDAR, aquisição/EEPROM térmica, driver/atitude IMU e histórico yaw. Nesta sessão térmica, matemática espacial, histórico yaw e três testes da bridge passaram. O driver IMU compilou sem erros com avisos tratados como erro, mas a execução do novo binário foi bloqueada pelo Controle de Aplicativo do Windows; esse resultado não é um teste executado com sucesso.

Na revalidação para a gravação do scanner, nove testes nativos compilaram com avisos tratados como erro. Sete executaram e passaram, incluindo driver IMU, térmica, parser LiDAR, referência pan, matemática de amostras/atitude IMU e diagnóstico EEPROM. Nessa rodada os novos executáveis de histórico yaw e matemática espacial foram bloqueados pelo Controle de Aplicativo do Windows, embora tivessem passado na rodada anterior. O registro distingue cada compilação de sua execução em `latest-scanner-install/native-tests/results.json`.

```powershell
# No diretório firmware:
python -B -m unittest test_serial_bridge
# Captura passiva, a partir da raiz e com PC na rede do scanner:
python tools/capture_scan.py --seconds 65 --output diagnostics/captura-unica
```

A captura passiva não inicia motores nem muda calibração. Gera CSV/JSONL/geometria com nome exclusivo e deduplica snapshots. `analyze_uwb_csv.py`, `analyze_uwb_profiles.py`, `fit_multiview_uwb.py` e `uwb_window_summary.py` analisam registros; `inspect_*` inspecionam CAD/netlists. Resultados offline não substituem referência física.

## 14. Histórico de erros, dificuldades e mudanças de plano

Este histórico descreve a evolução técnica com base nos registros preservados. As datas indicam as investigações e revisões documentadas, sem presumir que todo defeito começou naquele dia. Os documentos originais são fontes internas de evidência; o texto acadêmico final ainda precisa citar literatura, manuais e datasheets utilizados no método.

### 14.1 Proposta inicial e revisão de escopo — 20/09 em diante

A proposta inicial previa scanner em drone, nuvem térmica/RGB, posição UWB milimétrica e eliminação de drift. Planos posteriores acrescentaram EKF e metas elevadas de FPS, latência e redução geométrica. A auditoria encontrou um fluxo mais limitado: TCP para pontos, HTTP para imagens, UWB inicialmente por UDP, suavização escalar e voxelização, sem leitor USB Android naquele momento.

A dificuldade foi separar intenção e implementação. As metas não tinham medições que permitissem apresentá-las como capacidades concluídas; uma tag não fornecia heading e a base compacta não garantia exatidão espacial. A decisão passou a priorizar bancada estacionária, posição/direção verificadas e registro dos limites. Drone, RGB simultâneo, EKF e captura móvel ficaram como frentes futuras. Fontes: [especificação inicial](tcc.md), [plano inicial](implementation_plan.md) e [revisão técnica](docs/revisao-tecnica.md).

### 14.2 Geometria deformada e origem AR eram problemas diferentes — 22–24/09

Fotos mostraram estruturas deformadas mesmo com scanner e operador parados. O código usava uma escala de pulsos sem representar a redução mecânica, agrupava retornos com o mesmo ângulo de processamento e tratava a origem óptica como coincidente com o eixo. No aplicativo, uma amostra UWB antiga podia ser retransformada pela pose nova do celular, fazendo a origem acompanhar o observador.

Foram introduzidos histórico temporal de passos, pulsos por timer, origem óptica antes da rotação, convenções explícitas e transformação de cada medida uma vez. A marcação no apoio e a âncora AR passaram a estabelecer o referencial. Girar a nuvem por yaw global não corrige deformação interna. Escala/sentidos reais, folgas e perda de passos continuam dependentes de ensaio; essa correção antiga não prova a causa da torção observada em outubro. Fontes: [geometria e movimento](docs/diagnostico-geometria-e-movimento.md) e [novas capturas](docs/diagnostico-novas-capturas.md).

### 14.3 Rádios identificados, enlace e USB não eram estados equivalentes — 23–26/09

O diagnóstico inicial separou rádios reconhecidos de ranging completo. Fotos apresentavam POLL/RESPONSE sem FINAL e distâncias nulas. Foram revisados acesso SPI, seleção dos três rádios e etapas do DS-TWR. A identificação de um rádio passou a ser registrada separadamente de uma distância utilizável.

Também ficou claro que a porta USB da base era uma ponte CP210x, não CDC nativo. Foi necessário implementar host USB Android, permissão, reconexão e enquadramento de linhas. `@UWB28:` separou posições dos logs, e JSON manteve diagnóstico mesmo sem XYZ. A base normal passou a enviar diretamente pela USB, dispensando Wi-Fi/bridge no uso móvel. Nenhuma dessas mudanças, isoladamente, corrige multipercurso ou geometria ruim. Fontes: [histórico de integração](docs/integracao-historico.md) e [diagnóstico de integração](docs/integracao-diagnostico.md).

### 14.4 Da trilateração instantânea à localização estacionária multivista — 24–29/09

O enlace passou a entregar distâncias, mas parte das trincas era incompatível e a pequena separação entre antenas amplificava a incerteza. O requisito de base junto ao celular foi mantido; não se resolveu o problema afastando fisicamente as âncoras. Três âncoras coplanares também apresentam solução espelhada.

A mudança foi calibrar escala/offset por rádio e combinar ranges com vistas AR de uma tag estacionária e altura do apoio. Bugs que confundiam pose aproximada com multivista ou autorizavam captura com estado ambíguo foram corrigidos. Movimento livre ficou experimental e sem captura. Resíduo baixo ou candidato estável continuam insuficientes para afirmar erro absoluto pequeno. A geometria nominal antiga foi substituída pela medida de base 175 mm/lados 100 mm, portanto os resultados de incerteza de modelos anteriores não devem ser atribuídos à PCB revisada. Fontes: [calibração UWB](docs/uwb-calibracao-2026-09-26.md), [pose e fusão](docs/pose-fusao-2026-09-27.md) e [implementação temporal](docs/implementacao-fusao-2026-09-29.md).

### 14.5 Automação da direção não reconhecia a frente física — 28–30/09

A tentativa de alinhar automaticamente por raycast sobre o apoio podia produzir direção estável, porém diferente da frente do scanner. Profundidade informa uma superfície; não identifica sozinha o centro mecânico ou a orientação do equipamento. Alvos de profundidade antigos também prejudicavam a comparação.

A direção manual foi preservada, o indicador de yaw desconhecido passou a ser neutro e a tampa laranja original tornou-se um experimento de reconhecimento multivista com forma/cor e tempo coerentes. O ensaio por ponto no apoio permanece guiado pelo operador. Reconhecer uma região laranja ou um ponto de mesa não basta para declarar pose física correta. Fontes: [análise de deslocamento](docs/analise-deslocamento-2026-09-28.md), [ensaios de 30/09](docs/analise-testes-2026-09-30.md) e [implementação de pose/pan](docs/implementacao-pose-pan-2026-09-30.md).

### 14.6 Fluxo de calibração e estado de captura precisaram ser explicitados — 30/09–02/10

Os testes evidenciaram bloqueios e perdas de progresso entre posição, altura, direção e início de captura. Um perfil salvo não significava pose fixada; uma pose provisória não autorizava aquisição; enviar START não confirmava que os motores tinham iniciado. A interface foi reorganizada para expor os estados e a ação necessária em cada etapa.

A fixação do eixo tornou-se explícita, o zero pan passou a ter persistência coerente e as confirmações de comando passaram a recusar estados antigos. Essas decisões acrescentaram passos operacionais para evitar interpretações silenciosas. O perfil UWB foi preservado entre sessões, enquanto origem/direção AR precisavam ser refeitas na sessão correspondente. Fontes: [recuperação do fluxo](docs/recuperacao-fluxo-2026-09-30.md) e [análise dos testes de 02/10](docs/analise-testes-2026-10-02.md).

### 14.7 Timeout HTTP, reboot e relocalização AR exigiram respostas distintas — 02–04/10

Em 02/10 um atraso de estado apagou amostras e apoio enquanto AR ainda rastreava; o estado voltou pouco depois com a mesma referência. Em outra interrupção houve reboot real e zero inválido. Em 04/10 uma mudança grande de referência AR invalidou a direção, e o aplicativo recusou pontos enquanto o scanner ainda varria.

A correção preservou progresso espacial durante ausência temporária de estado de um scanner antes válido e declarado parado, mantendo a captura bloqueada até conferir a retomada. Reboot, zero perdido e tracking/referencial incompatíveis continuam exigindo revalidação. O HUD passou a avisar quando o scanner mede, mas a nuvem não aceita pontos. Não se atribuiu a causa física do salto AR sem evidência. Fontes: [auditoria do tempo de calibração](docs/auditoria-tempo-calibracao-2026-10-02.md) e [diagnóstico de fusão térmica](docs/diagnostico-fusao-termica-2026-10-04.md).

### 14.8 Subpáginas e calibração parcial da MLX90640 — 30/09–03/10

O caminho térmico confundia duas leituras com um quadro completo, mesmo quando repetia a mesma subpágina. Havia espera sem prazo total e metade dos pixels podia ficar sem valor válido. Leituras repetidas da EEPROM da unidade confirmaram 18 pixels sem calibração utilizável; não foi determinada a origem temporal desse conteúdo.

A aquisição passou a exigir metades 0/1 coerentes e prazos, e o modo parcial conservou 750 pixels elegíveis com máscara explícita. O endpoint `/thermal/masked` e a transparência na prévia mostraram a ausência de dados sem inventar temperatura. Essa recuperação não repara EEPROM nem certifica precisão dos pixels restantes. Ensaios só por USB, com alimentação principal desligada, foram registrados separadamente do funcionamento alimentado. Fontes: [análise térmica](docs/analise-termica-2026-10-01.md) e [recuperação do fluxo térmico](docs/recuperacao-fluxo-2026-09-30.md).

### 14.9 Abrir a prévia encontrou um defeito de pilha HTTP — 03/10

Foram observados resets e reconexões ao investigar a prévia. A análise do binário encontrou dois quadros de função conhecidos somando 8.464 bytes para uma tarefa com 8.192 bytes de pilha. Um buffer de estado permanecia na pilha ao atender outras rotas, tornando esse caminho concretamente inadequado.

O buffer passou a ser estático e pertencente à tarefa HTTP, reduzindo a soma conhecida para 5.392 bytes. Após a gravação, um ensaio parado recebeu 100 de 100 imagens distintas sem erro HTTP/reset detectado. O teste foi curto e não mediu o pico total de pilha em carga, portanto não demonstra estabilidade de toda sessão com motores. Fonte: [queda da prévia térmica](docs/queda-preview-termica-2026-10-03.md).

### 14.10 Referência aceita e GY-25: leitura, repouso e aplicação — 03–04/10

Uma sessão aceita pelo usuário foi preservada na tag `scanner-baseline-20261003-1614`. A estimativa visual de cerca de 10 cm de diferença e yaw manual permitiu manter uma referência de comparação, sem convertê-la em medida independente de precisão.

A integração da GY-25 encontrou diferenças de campos e uma dependência circular: o aplicativo exigia um estado que só aparecia após a ativação do modo. Depois separou leitura, bias, referência e habilitação. A referência parada mediu o resíduo do gyro; foram observadas variações de −10,272° em 39,690 s antes e −0,420° em 59,115 s no ensaio posterior. O aquecimento não foi controlado, impedindo atribuir toda a diferença a uma única causa. Yaw continuou relativo ao alinhamento inicial. Capturas de 04/10 registraram acompanhamento desligado, logo não sustentam atribuir sua torção à integração de yaw da GY-25. Fontes: [versão de referência](docs/versao-referencia-2026-10-03.md), [GY-25 e LOD](docs/gy25-quads-lod-2026-10-03.md) e [ensaio de fusão](docs/diagnostico-fusao-termica-2026-10-04.md).

### 14.11 Alvo térmico de 8 FPS não equivalia a 8 FPS durante captura — 03–04/10

Ensaios em repouso observaram aproximadamente 7,8 quadros/s, mas capturas de 04/10 mostraram redução para cerca de 3,9 quadros/s e associação térmica em aproximadamente 4,1% dos retornos LiDAR. Esse percentual é de eventos/retornos do firmware, não de voxels ou pixels. Na política gravada naquele momento, três cópias incoerentes consecutivas reduziam o alvo e ele permanecia reduzido.

O código também consultava a pose atual para um quadro anterior, um defeito distinto do desempenho. A fusão foi corrigida para os dois instantes, e a tarefa térmica ganhou prioridade acima da fusão, com tempo RAM separado no diagnóstico. Pressão de processamento e checkpoint inicial foram hipóteses compatíveis com os logs, sem contribuição isolada de cada causa. Campo óptico, máscara, janela de 200 ms e ausência de profundidade continuam limitando cobertura. A versão-fonte posterior que mantém o alvo após overruns ainda precisa de ensaio de taxa real durante giro. Fontes: [revisão 8 FPS](docs/revisao-termica-8fps-2026-10-03.md), [fusão térmica](docs/diagnostico-fusao-termica-2026-10-04.md) e [flickering/cadência](docs/diagnostico-flickering-termica-2026-10-04.md).

### 14.12 Superfícies maiores, flickering e publicação gradual — 29/09–04/10

O LOD evoluiu de pontos pequenos para quads de 12,5 mm e retângulos com suporte medido. O cálculo de nuvens grandes no Editor levou centenas de milissegundos, motivando execução em segundo plano. Foram identificadas invalidação global por mudança local, cobertura incorreta de slot reutilizado e uso do raio de aquisição como normal de um quad, que podia desaparecer ao caminhar paralelamente à parede.

As correções conservaram regiões compatíveis, retiraram apenas retângulos afetados, verificaram identidades/épocas e mantiveram quads pelos dois lados. Nesta sessão a revisão encontrou outro defeito: a temperatura e a posição internas mudavam gradualmente, mas a apresentação comparava apenas cada incremento anterior. O novo teste reproduziu aquecimento de 20 a 28 °C e refinamentos pequenos acumulados; a correção compara o último estado publicado, conserva a filtragem de ruído e passou nas validações e no visor real em Play Mode. Aparência/FPS no Android e a torção física continuam a verificar. Fontes: [evolução do LOD](docs/gy25-quads-lod-2026-10-03.md), [diagnóstico regional](docs/diagnostico-flickering-termica-2026-10-04.md) e logs desta revisão em `diagnostics/20261004/latest-phone-install/refined/`.

### 14.13 Dificuldades do ambiente de desenvolvimento e entrega

O histórico inclui uma tentativa bloqueada pelo serviço de licença Unity, uma etapa nativa interrompida após longa pausa do computador e testes nativos bloqueados pelo Controle de Aplicativo do Windows. Esses eventos devem aparecer como limitações/reexecuções do ambiente, separados de falhas de lógica do scanner. Uma mensagem de classe opcional ausente durante startup também não deve ser chamada de crash quando o processo continuou e exibiu o menu.

Na revisão atual, as duas cenas e a suíte passaram, o APK foi substituído com assinatura compatível e seu hash instalado foi conferido. O telefone estava bloqueado, limitando a inspeção visual de menu/AR no aparelho, embora o processo tenha iniciado. A compilação e a instalação são evidências de integração de software; não substituem uma captura física. Fontes: [integração histórica](docs/integracao-historico.md), [implementação de 30/09](docs/implementacao-pose-pan-2026-09-30.md) e registros locais de build/instalação.

### 14.14 Como utilizar este histórico no TCC

Para cada experimento, relatar objetivo, configuração/versões, condição de alimentação, equipamento parado ou girando, procedimento, métricas e limite da conclusão. Separar defeito comprovado de código, observação física e hipótese. Resíduo do estimador, diferença entre duas poses internas ou estabilidade de uma série não equivalem a erro contra ground truth.

As mudanças de plano são resultados do processo de engenharia: USB direto substituiu a bridge no uso normal; localização estacionária multivista substituiu a promessa de trilateração compacta suficiente; alinhamento manual complementou a orientação não observável pelo UWB; máscara parcial substituiu a imagem térmica falsamente completa; LOD regional/assíncrono substituiu reconstrução global frequente. Esses resultados podem ser discutidos como decisões justificadas, preservando os recursos ainda não demonstrados como limitações e trabalhos futuros.

### 14.15 Gravação das alterações recentes e conferência em repouso — 04/10

Após o usuário conectar o scanner, a serial confirmou o firmware anterior com inclinação desligada e térmica próxima de 7,8 quadros/s. O firmware da árvore atual compilou e foi gravado no ESP32-S3 identificado, com hashes dos blocos verificados. Um backup preservou boot, NVS e aplicação anterior; a leitura da NVS após a gravação foi idêntica à anterior.

A conferência posterior observou IMU pronta com inclinação ativa e cerca de 7,81 quadros térmicos/s, sem incremento dos contadores de erro durante a janela medida. Houve ocorrências na inicialização, explicitadas no estado desta revisão. Essa etapa atualiza o equipamento e confirma sensores em repouso, mas não testa o efeito da compensação na nuvem, aciona motores ou comprova 8 FPS sob carga. A condição de alimentação principal não foi inferida da conexão USB. Fonte: `diagnostics/20261004/latest-scanner-install/validation.json`, logs de gravação e séries temporais por serial.

### 14.16 Desempenho do LOD e controles de visualização — 04/10

Após outro ensaio, o usuário considerou a aparência aceitável, mas relatou lentidão acentuada com LOD. Esse relato é uma observação de uso; não houve medição comparativa de FPS sob condições controladas. A revisão encontrou que desligar somente a malha ainda permitia o cálculo de geometria quando o tamanho adaptativo estava ativo. A decisão foi iniciar em somente pontos e tornar o LOD opcional, interrompendo novos cálculos e descartando resultados antigos ao desligar.

Foram adicionados fatores explícitos de tamanho, aplicação imediata com captura pausada e a ocultação experimental pelo lado de aquisição. A visibilidade pelos dois lados permanece padrão porque raios de aquisição não são normais físicas confiáveis. Todos os ajustes interativos do visor foram reunidos no menu direito; estados permanecem à esquerda. O histórico anterior é preservado: a solução evoluiu de simplificação sempre ativa para uma escolha do operador diante do custo observado no aparelho.

A primeira rodada passou no visor em Play Mode, mas a mesma regressão falhou na checagem de compilação sem interface gráfica: a fixture deixava o ParticleSystem inativo e sem inicialização nativa no modo de edição. A correção isolou e inicializou o sistema de partículas, conservando a exigência de ler as 16 partículas reais e seus tamanhos imediatamente após desligar LOD. Também foi corrigido o descarte de meshes/recursos no modo de edição, usando `DestroyImmediate` nessa condição. A validação isolada voltou a passar sem esses avisos; a primeira tentativa e a repetição ficam em `diagnostics/20261004/display-controls/`. A revisão da interface encontrou ainda o fechamento RGB condicionado à presença de imagem/estado recente; o botão agora permanece disponível quando a prévia falha ou o scanner desconecta. Iniciar/pausar foi colocado no começo da rolagem para acesso direto.

A rodada final passou no menu e no visor reais em Play Mode e nas onze validações antes do build. O cálculo sintético de 80 mil pontos levou 568 ms no Editor nessa execução; não é FPS medido no Android. O APK foi instalado por atualização às 23:33 de 04/10, com assinatura correspondente, hash instalado idêntico e primeira instalação/permissão de câmera preservadas. O APK anterior desta sessão, instalado às 22:09, tinha 36.552.357 bytes e SHA-256 `edc2bf1ec4125ae8391fdf8e4771b88ff8e444ae9adfa58f21a2400dc1edf78d`; foi mantido como backup. O novo processo iniciou sem falha fatal, mas a tela bloqueada impediu inspecionar a interface no Android. Evidências e limitações constam nos JSONs/logs de `diagnostics/20261004/display-controls/` e `refined/`; o relato do ensaio anterior não substitui um ensaio da nova versão.

## 15. O que falta para finalizar

| Frente | Trabalho necessário | Evidência de conclusão |
| --- | --- | --- |
| Geometria mecânica | Medir origem óptica, braço da tag, offset câmera/placa, altura, sinais dos eixos e pulsos por volta; resolver nominal 79 mm versus medida histórica 90 mm. | Alvos/ângulos/distâncias independentes e erro documentado, sem realinhar uma nuvem antiga. |
| Inclinação/GY-25 | Validar novas referências nominais, filtro, idade admitida, eixo de yaw e perda/recuperação durante giro. | Ensaios de inclinações conhecidas, repouso e pan completo, com logs e comparação angular externa. |
| Térmica em carga | Medir taxa real, RAM, overruns, descartes e associações com LiDAR+pan+HTTP ativos. | Captura comparável com duração definida; alvo 8 FPS separado da taxa realmente obtida. |
| Fusão térmica | Conferir perfil óptico e extrínsecos com alvo quente interceptado pelo LiDAR; estudar paralaxe/oclusão. | Correspondência geométrica/temporal em dois pans e diferentes distâncias, incluindo anteparo próximo. |
| UWB | Calibrar rádios e validar em vistas/distâncias não usadas no ajuste; medir incerteza e efeito da PCB compacta. | Ground truth de posição e distribuição de erros/recusas, com critério de aceitação previamente definido. |
| Android/AR/LOD | Ensaiar tracking perdido/retomado, desconexão, parada, perfis, exportação e nuvem grande no aparelho. | Registros de FPS, latência, memória, ausência de exceções e preservação das regiões medidas. |
| Reprodutibilidade | Congelar fonte/dependências/configuração, identificar APK/firmware por hash e separar fonte de versão gravada. | Manifesto de release, scripts/validações e conjunto de evidências reproduzível. |
| Publicação | Definir nome/package, versão/código crescente e chave de assinatura com plano de migração. | Atualização do mesmo aplicativo com dados preservados e identificação inequívoca de versão. |
| Texto acadêmico | Descrever método, hipóteses, métricas, resultados, limitações e trabalhos futuros, citando referências técnicas externas. | Conclusões sustentadas por medidas; metas iniciais não apresentadas como resultados. |

Scanner móvel/voo, fusão RGB simultânea, heading absoluto, EKF e tratamento completo de oclusão permanecem fora do conjunto demonstrado. Podem ser delimitados como trabalhos futuros ou implementados e ensaiados em etapas próprias. A meta histórica de 10 cm/10° precisa de medição independente; a estimativa visual aceita em um teste não equivale a certificação dessa meta.

Para o ensaio final, usar primeiro scanner estacionário, origem/direção conferidas, alvo geométrico e térmico conhecido e uma configuração por captura. Guardar versões, geometria, perfil UWB, vídeo, CSV/JSONL, tempos e condições de alimentação. Depois comparar versões nas mesmas condições. Essa sequência permite atribuir resultados a mudanças específicas e apresentar o TCC com limites verificáveis.

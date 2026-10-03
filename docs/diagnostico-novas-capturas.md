# Novas capturas: diagnóstico e teste de bancada

Atualização de 23/09/2026. Complementa o [relatório de geometria e movimento](diagnostico-geometria-e-movimento.md). Foram consideradas as novas capturas enviadas, incluindo as duplicadas, a foto do anúncio S3 N16R8 CAM/OV2640 e o print da PCB de suporte. O usuário confirmou que **scanner e pessoa estavam parados**, e que atualizou as duas placas e o APK. A câmera RGB está no conector flat da própria placa ESP32-S3-CAM.

**Conclusão atual:** há correções implementadas e um APK compilado para separar as causas. Não foi realizado ensaio físico nesta revisão; não é possível afirmar pelas fotos que a espiral foi eliminada, que o UWB mede corretamente ou que a câmera já produz imagem.

## O que as novas imagens permitem concluir

As fotos mostram contornos reconhecíveis do ambiente junto a estruturas fortemente deformadas no centro. O scanner recebe pontos e informa RPM interno e pan por contagem de pulsos. Isso comprova recepção, mas não que o ângulo informado corresponde ao giro físico da cabeça.

Há dois problemas distintos a medir:

1. **Forma da nuvem:** sentido/escala/zero do pan, plano do LiDAR, origem óptica excêntrica, sincronismo e possíveis retornos no próprio suporte. Alterar o yaw global gira a nuvem inteira; não desfaz uma espiral interna. Uma pessoa parada também deve ser reconstruída sem essa torção. As fotos não permitem identificar com certeza quais raios atingiram a pessoa, a cadeira ou o próprio equipamento.
2. **Sobreposição na câmera do celular:** a prévia começava em uma posição arbitrária do mundo AR. Um erro de translação causa desalinhamento aparente maior em objetos próximos. Reconhecer uma parede distante não prova que a origem da nuvem está no scanner. Marcar a origem no apoio ajuda a testar essa hipótese, mas não corrige a geometria interna.

O yaw de montagem do firmware foi preservado em **0°**, como estava nos testes mais recentes. O modelo nominal havia indicado 90° na convenção adotada. Essa diferença é pendência de calibração: `LIDAR_MOUNT_YAW_DEG` altera o plano do sensor em relação à excentricidade; o yaw do aplicativo ajusta a orientação da nuvem no ambiente. Não são ajustes equivalentes.

## Alterações entregues nesta rodada

| Área | Correção / instrumento de diagnóstico |
|---|---|
| Giro externo | Comando separado para parar o eixo externo mantendo a medição LiDAR; botão **Teste 2D** no aplicativo |
| Dados para análise | `/scan.csv` guarda os últimos 1.024 raios aceitos antes da projeção 3D, com distância, ângulo LiDAR e pan estimado; script de captura contínua no PC |
| Alinhamento AR | **Marcar posição do scanner**, ajuste da altura da origem e yaw fino; identifica quando a origem ainda é arbitrária |
| Base UWB | Diagnóstico periódico independente da posição: rádios detectados, distâncias, etapa de falha e incerteza |
| Comunicação da base | Reconexão Wi-Fi mesmo sem rádios inicializados; tentativas de inicialização sem silenciar permanentemente a base |
| Serial da base | Posição enquadrada como `@UWB28:` + hexadecimal + fim de linha; a bridge ignora logs e mensagens incompletas |
| RGB | Driver JPEG restaurado, validação de pinos, perfil separado para S3-CAM e tarefa de captura independente da espera de quadros térmicos |
| PWM / câmera | Canal de PWM do motor LiDAR separado explicitamente do clock da câmera |
| Térmica | Estado de inicialização, erro, quantidade e idade de quadros; rejeição de quadros incompletos, aquisição em 8 Hz e reinicialização após falhas retornadas |
| App | HTTP 503 apresenta o motivo enviado pelo scanner; temperaturas ausentes não aparecem como 999/-999 |

## Câmera RGB e conflito de pinos

O print recebido mostra a **PCB de suporte**. A conexão interna do flat pertence à placa S3-CAM encaixada nela, portanto não aparece na netlist dessa PCB. A ausência de OV2640 na netlist não significa que a câmera física esteja desconectada.

O anúncio identifica ESP32-S3 N16R8 + OV2640, mas não documenta a revisão elétrica. O perfil de teste usa a pinagem comum compatível com S3-EYE/Freenove: XCLK15, SDA4, SCL5, D0–D7=11/9/8/10/12/18/17/16, VSYNC6, HREF7, PCLK13, sem PWDN/RESET controlados. É um **perfil candidato**, ainda não confirmação da sua unidade. [Pinagens publicadas pela Espressif](https://github.com/espressif/arduino-esp32/blob/master/libraries/ESP32/examples/Camera/CameraWebServer/camera_pins.h).

Se essa pinagem for confirmada, existe conflito com a PCB atual: a tag UWB usa **MOSI4/MISO5**, os mesmos sinais SDA/SCL da câmera. O perfil RGB de teste desativa a tarefa UWB e mantém CS42 alto. O DWM1000 deixa MISO em alta impedância quando não selecionado, permitindo testar a câmera isoladamente com o rádio corretamente alimentado. [Datasheet DWM1000, interface SPI](https://store.qorvo.com/datasheets/qorvo/dwm1000datasheet.pdf).

Isso **não resolve o funcionamento simultâneo**. Para câmera e UWB juntos, será necessário confirmar o esquema da S3-CAM e rerotear os sinais conflitantes para GPIOs livres compatíveis. Não foi escolhida uma nova ligação sem verificar a placa. A biblioteca SPI da tag agora usa os mesmos pinos de `config.h`, evitando que uma futura mudança no arquivo de configuração seja ignorada por constantes internas.

Perfis disponíveis:

- `esp32s3`: scanner normal, UWB ativo, RGB sem pinagem confirmada e desativado.
- `esp32s3_rgb_test`: pinagem candidata RGB ativa, **tag UWB desativada intencionalmente**.

## Sequência de teste recomendada

### 1. Firmware normal e teste de forma

Instale `Builds/ScannerAR-diagnostico-20260923.apk` e atualize scanner e base com os perfis normais. Os comandos abaixo partem da raiz do projeto e gravam apenas a placa correspondente; confira a porta se houver duas conectadas:

```powershell
pio run -d firmware/scanner -e esp32s3 -t upload
pio run -d firmware/viewer -e esp32dev -t upload
```

Use a prévia local e mantenha scanner, suporte e alvos imóveis. Comece com uma parede e uma caixa/cadeira estática, sem mexer na calibração durante a coleta.

1. Inicie o scan e pressione **Teste 2D: parar eixo externo**. Aguarde o eixo parar e pressione **Limpar**. O LiDAR interno continua girando.
2. Observe o corte fixo. Se ele já estiver deformado, investigue distância/ângulo LiDAR, leituras do próprio suporte e protocolo antes de culpar a rotação externa. Uma rotação global do corte não significa erro na sua forma.
3. Retome o eixo externo a 1 RPM, aguarde estabilização e limpe novamente. Marque a posição física inicial da cabeça e compare 90°, 180° e uma volta física com a telemetria **Pan (pulsos)**. Considere o wrap 359°→0° e o fato de o zero ser relativo ao boot.
4. Uma volta estabilizada a 1 RPM deveria levar aproximadamente 60 s **se** relação de transmissão, micropassos e passos do motor corresponderem à configuração. Se não corresponder, corrija essa escala antes de ajustar offsets ou filtrar pontos.

O perfil nominal usa 180T/10T, 200 passos e 8 micropassos, ou 28.800 pulsos/volta. Não há encoder: o programa não detecta perda de passo, folga ou deslocamento manual da cabeça. O movimento contínuo de uma cabeça excêntrica requer a translação já incluída no modelo, mas seus 79 mm nominais e o centro óptico precisam de medida real.

### 2. Alinhar a sobreposição AR

Com a coleta parada, aponte o centro da tela para o plano de apoio no ponto sob o eixo do scanner e pressione **Marcar posição do scanner**. Aguarde o AR reconhecer chão/mesa. A origem nominal do modelo está no apoio, portanto comece com altura **0 cm**; não adicione outra vez a altura óptica de 14,7 cm já aplicada no firmware.

Ajuste o yaw fino pela orientação de uma parede conhecida. A marca é manual e depende da precisão do plano AR; não mede orientação completa nem acompanha movimento posterior do scanner. Os controles limpam a nuvem para evitar misturar calibrações. Não use a coincidência de apenas uma parede distante como critério de sucesso: confira também um objeto próximo, mudando o ponto de vista do celular.

### 3. Capturar os dados para verificar a espiral

Conecte o PC à mesma rede `ArScanner_Net`, mantenha o aplicativo controlando o scan e execute uma captura com um nome novo:

```powershell
python tools/capture_scan.py --seconds 65 --output captura_pan_1rpm
```

São criados `.csv` (raios) e `.jsonl` (estado e erros HTTP). O script não inicia motores. Faça uma captura com o eixo parado e outra girando. É possível abrir diretamente `http://192.168.4.1:8889/scan.csv` para um snapshot.

`pan_input_deg` é derivado dos pulsos, não de encoder. `sample_us` é uma estimativa do instante de aquisição no relógio do scanner. O buffer é circular; a consulta HTTP pode perder raios, e lacunas na sequência identificam isso. Captura HTTP também acrescenta carga; não é uma gravação UART sem perdas.

### 4. Separar rede, rádio e qualidade do UWB

Use o perfil normal do scanner. A base deve enviar diagnóstico mesmo sem posição aproveitável:

| Mensagem | Interpretação / próximo teste |
|---|---|
| Sem dados da base | Verificar Wi-Fi da base, alimentação e UDP9999; ler serial da base |
| Rádios SPI / máscara diferente de 7 | Pelo menos um dos três rádios não inicializou; bits 1/2/4 correspondem às âncoras 1/2/3 |
| TWR incompleto | Rádio inicializou, mas alguma troca não terminou; verificar tag, alimentação, versões e códigos abaixo |
| Distâncias incompatíveis | Há medições, mas não fecham com a geometria configurada |
| Incerteza alta | A base está comunicando; a geometria/ruído não permite aceitar a posição |
| Posição experimental | Posição recente aceita; não significa pose completa calibrada |

Etapas por âncora: **0** sucesso; **1** rádio ausente; **2** timeout de envio POLL; **3** timeout de RESPONSE; **4** cabeçalho inválido; **5** timeout de FINAL; **6** instante de FINAL diferente do programado; **7** REPORT ausente/inválido ou distância rejeitada.

O limite de incerteza voltou de 12 m (ensaio anterior) para 0,50 m. Aumentar esse limite permite desenhar posições ruins, mas não melhora a medição. As três âncoras próximas na placa amplificam muito o erro e têm ambiguidade de lado do plano. O relatório anterior quantifica essa limitação; uma tag mais MPU6050 também não oferece orientação absoluta completa.

**USB direto no Android ainda não está implementado.** A alternativa existente é a base ligada por USB ao PC e o PC encaminhando para o IP do celular, ambos na rede do scanner:

```powershell
python firmware/serial_to_udp_bridge.py COM5 --host IP_DO_CELULAR
```

Substitua porta/IP pelos valores reais. A bridge requer `pyserial`. O formato serial mudou: use a bridge e o firmware novos juntos. Ela agora ignora os logs em vez de interpretá-los como coordenadas. Não rode ao mesmo tempo um monitor serial na mesma porta.

### 5. Testar a RGB isolada

Confira alimentação e cabo flat. Grave somente o scanner com:

```powershell
pio run -d firmware/scanner -e esp32s3_rgb_test -t upload
```

Solicite **Drone óptico RGB** no app ou abra `http://192.168.4.1:8889/rgb`. Consulte `/status`: deve informar `rgbProfile=1`, `tagEnabled=false` e o motivo de eventual falha RGB. Se aparecer imagem, o perfil candidato funciona nessa unidade. Se não aparecer, registre `rgbMessage` e o erro `esp_camera_init` da serial; não conclua automaticamente que a câmera está defeituosa.

Depois, restaure `esp32s3` para testar UWB. Não espere ranging com `esp32s3_rgb_test`. Os perfis não reparam as trilhas compartilhadas.

### 6. Testar a térmica

Use `/thermal` e acompanhe `/status`: `thermalReady`, `thermalError`, `thermalFrames`, `thermalAgeMs`. Erro -100 indica falha de inicialização; -101 indica leitura incompleta/não finita; outros valores vêm do driver. A idade UINT32_MAX significa nenhum quadro disponível.

A netlist mostra MLX/MPU/UWB alimentados pela rede externa ligada a J4.3, distinta do 3V3 do ESP. USB no ESP não comprova que esses sensores recebem a alimentação correta. O print da PCB não permite medir tensão, continuidade ou solda. Verifique isso em bancada antes de interpretar HTTP503 como falha de rede.

O driver tenta recuperar falhas que retornam da biblioteca; uma chamada que ficar bloqueada internamente aguardando dados ainda pode impedir atualização. As imagens térmicas/RGB são prévias: a associação de temperatura/cor a cada raio continua desativada até calibrar lentes, montagem e tempo.

## Verificação realizada e limites

- Compilados scanner normal, scanner RGB de teste e base UWB com PlatformIO.
- APK Android ARM64/IL2CPP gerado com Unity 6000.5.4f1; compilação do código C# concluída.
- Testes nativos de geometria, histórico, DS-TWR, trilateração, buffer de captura e comando de parada do eixo concluídos.
- Dois testes Python da bridge passaram, incluindo fragmentação, logs misturados e recuperação de mensagens inválidas.
- O parser LiDAR havia passado na rodada anterior; sua reexecução nesta rodada foi bloqueada pelo controle de aplicativos do Windows. Não foi contornado esse bloqueio.
- Nenhuma placa foi gravada por esta revisão e o APK não foi executado no celular. Restam validação física da pinagem, giro, medidas UWB e imagens reais.

O objetivo de mover livremente scanner e visualizador permanece pendente de orientação, sincronização e rastreamento espacial adequados. O novo teste de bancada permite obter evidência para corrigir a deformação sem atribuir precisão inexistente aos sensores.

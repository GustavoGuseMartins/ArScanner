# Montagem da GY-25 no vídeo de 03/10/2026

## Evidência examinada

O vídeo mais recente da câmera do celular conectado foi copiado de `/sdcard/DCIM/Camera/20261003_131422.mp4`, preservando o original. A cópia está em `diagnostics/20261003/imu-mount-video/20261003_131422.mp4`: 260.891.259 bytes, duração aproximada de 50,72 s, SHA256 `20e49e4ce10b72a2469a4953e0b5f7154af0d6b18a965f76f427cf4ec66b1371`. O vídeo é 3840×2160 com rotação de apresentação; os quadros foram extraídos com essa rotação aplicada, sem espelhamento adicional.

Foram examinados 17 quadros em intervalos de três segundos, além de detalhes na resolução original. A [folha de contato](../diagnostics/20261003/imu-mount-video/contact-sheet.jpg) organiza os intervalos; os rótulos são faixas de amostragem, não instantes exatos de cada gesto. A fala foi transcrita automaticamente em português, inteiramente no computador, com janelas sobrepostas. O [texto bruto](../diagnostics/20261003/imu-mount-video/transcript.txt) contém erros de reconhecimento de nomes como LiDAR/GY-25; não foi tratado como leitura precisa de sinais ou nomes de eixos.

## O que o vídeo confirma

- A GY-25 azul está soldada à PCB vertical da cabeça, acompanhando o giro do conjunto.
- O lado dos componentes da GY-25 fica exposto para o lado do ESP32, oposto ao lado do LiDAR laranja.
- A fileira `GND / TX / RX / VCC` fica para cima, e `RST / B0 / SCL / SDA` para baixo. O [detalhe do módulo](../diagnostics/20261003/imu-mount-video/gy25-closeup.jpg) registra essas marcações.
- Comparada à Figura 1 do [manual original GY-25](https://www.gysensor.cn/wp-content/uploads/GY-25%E5%80%BE%E6%96%9C%E8%A7%92%E5%BA%A6%E4%BC%A0%E6%84%9F%E5%99%A8%E6%A8%A1%E5%9D%97%E4%BD%BF%E7%94%A8%E6%89%8B%E5%86%8C2.pdf), a apresentação do módulo na PCB corresponde a um giro de 90° no plano. Isso descreve a placa e seus conectores; não certifica automaticamente a orientação interna do sensor ou uma transformação de montagem.
- A explicação reconhecida no áudio confirma que o módulo está soldado à PCB, que fica na vertical. Os termos “cima dela”, “para frente” e “para lá” dependem do gesto e não nomeiam X/Y/Z do sensor.

Não foi possível identificar de forma inequívoca a marca do pino 1 no encapsulamento da MPU a partir dos quadros. A [especificação original MPU-6000/6050, revisão 3.4](https://product.tdk.com/system/files/dam/doc/product/sensor/mortion-inertial/imu/data_sheet/mpu-6000-datasheet1.pdf), seção 11.1/página 40, referencia a orientação dos eixos a essa marca. **Atualização com o diagrama enviado pelo usuário:** a face exposta do CI central é compatível com o encapsulamento QFN24 da MPU6050, e o diagrama fixa +Z saindo dessa face. Em conjunto com a PCB vertical, o módulo alinhado a ela e a aceleração predominantemente em −X já observada, é possível inferir a correspondência de eixos sem tratar a ausência de uma marca de pino 1 legível como impedimento a toda inferência.

## Mapeamento da montagem

As leituras USB anteriores à gravação tinham aceleração próxima de `(-0,934; -0,016; +0,290)` g. A componente dominante sugere **−X do sensor aproximadamente para cima**, mas não estabelece uma pose nivelada: essas amostras não são simultâneas aos gestos do vídeo, e a componente Z é significativa.

A combinação dessas evidências, considerando o CI visível como a MPU6050 e a montagem normalmente apoiada nas leituras anteriores, fornece a correspondência abaixo. A direita é definida na vista traseira do scanner, pelo lado da PCB mostrado no vídeo; a traseira é o lado exposto do CI, oposto ao LiDAR.

| Eixo positivo do sensor | Direção na cabeça do scanner |
| --- | --- |
| +X | Para baixo |
| +Y | Para a direita |
| +Z | Para trás, saindo da face do CI |

No referencial físico de mão direita da cabeça:

```text
headRH = (sensorY, -sensorX, sensorZ)
R = [ 0  1  0
     -1  0  0
      0  0  1 ]
```

É uma rotação própria (determinante +1), fundamentada pela montagem e leituras. **Atualização após o ensaio físico de 03/10:** a pose com a face do CI para cima confirmou +Z, a inclinação para a direita confirmou o sentido de Y e o retorno ficou a 1,06° do apoio inicial. O [relatório do ensaio](ensaio-montagem-gy25-2026-10-03.md) sustenta essa correspondência de canais/sinais e registra as limitações de bias/escala e deriva. Ainda não foi validado acompanhamento dinâmico do pan. Para Unity, a conversão de aceleração é `(sensorY, -sensorX, -sensorZ)` e a de giro `(-sensorY, sensorX, sensorZ)`. A reflexão de convenção não se aplica com os mesmos sinais aos vetores de aceleração e giro. Essas expressões não foram inseridas no firmware nem usadas para alterar a nuvem nesta análise.

Uma consequência concreta é que o giro em torno da vertical do scanner corresponde principalmente ao **gyroX do sensor**. Quando o scanner está nivelado, yaw horário visto de cima corresponde a gyroX positivo. O `angleZ` atual do driver integra o Z do sensor, portanto representa outra direção física nessa montagem. Os ângulos úteis devem ser calculados após converter aceleração e gyro para os eixos da cabeça; simplesmente trocar os nomes de pitch/roll/yaw do sensor não equivale a essa transformação.

A “frente” usada pelo referencial de scanner no código é a direção do eixo de pan ao LiDAR. A normal exposta da placa e a direção óptica da câmera térmica não são sinônimos automáticos dessa frente. Foi solicitada uma confirmação textual sobre o gesto final, sem presumir que ele define um eixo interno da MPU.

## Verificação mínima para usar os dados

O vídeo, o diagrama e o ensaio físico com poses confirmadas pelo operador sustentam a correspondência dos eixos como base da integração. Foram coletados apoio normal, primeira inclinação inconclusiva, face do CI para cima, inclinação lateral e retorno. A confirmação de canais/sentidos foi concluída; a precisão angular, o bias/escala do acelerômetro e o giro dinâmico permanecem etapas distintas. Não usar a correspondência confirmada como certificado de toda a orientação do conjunto.

Enquanto isso, normas de aceleração e velocidade angular já permitem avaliar repouso sem depender do mapeamento. Para um giro confirmado exclusivamente em torno da vertical, a projeção do gyro sobre a gravidade pode verificar giro relativo; não fornece direção inicial absoluta em AR. Esses usos ainda são possibilidades de integração, não funções de calibração automática ativadas nesta análise.

`IMU_APPLY_TILT` continua desligado. Antes de aplicar inclinação aos pontos, também conferir a composição com o pan, a idade da amostra na aquisição LiDAR e a ordem da reflexão vertical já aplicada pelo aplicativo. Preservar a pose UWB e o yaw manual que funcionaram. Não houve gravação de firmware, alteração de APK, instalação no celular ou comando de motor nesta análise do vídeo.

# Fluxo de medição UWB, LiDAR e térmica

Revisão de 26/09/2026 após o ensaio com o visualizador e o scanner gravados. A recusa da posição observada no aplicativo é real: três alcances chegaram, mas não demonstraram uma pose 3D suficientemente confiável. Sem pose aceita ou marcação manual, o eixo do scanner é ocultado para não sugerir uma posição falsa.

## O que cada processador faz hoje

| Etapa | Processador | Dados e limite |
| --- | --- | --- |
| Distância UWB | ESP32 do visualizador | Interroga DWM2, DWM3 e DWM4 em sequência com DS-TWR; valida a troca completa e publica as três distâncias brutas e o estado por USB serial a 115200 baud. |
| Posição UWB local | ESP32 do visualizador | Tenta interseção das três esferas. Se a geometria passa no limite de incerteza, aplica suavização Kalman escalar por eixo e envia `@UWB28`. Esta posição é apenas diagnóstica para o modo AR atual. |
| Posição em AR | Celular | Lê os alcances do diagnóstico USB, aplica o perfil de calibração de cada rádio, usa a pose AR da câmera e a montagem estimada da PCB para tentar a pose no mundo. Rejeita trincas incompatíveis, ambiguidade e incerteza acima de 0,5 m. Para scanner parado, a marcação manual AR do apoio já funciona. |
| Amostras LiDAR | ESP32-S3 do scanner | Valida quadros do LiDAR, associa ângulo do pan pelo instante da amostra, projeta feixe em XYZ do eixo mecânico e envia registros de 28 bytes em lotes TCP. O ajuste de inclinação por IMU permanece desligado até confirmar sinais e eixos. |
| Cor térmica | ESP32-S3 do scanner | Projeta amostra LiDAR no campo da MLX90640, usa quadro recente e marca explicitamente pontos sem temperatura válida. RGB está desativada no perfil UWB normal por conflito de GPIO. |
| Nuvem e visualização | Celular | Converte os pontos locais usando a pose aceita do scanner, mantém observações no mundo AR, agrega amostras na mesma célula espacial de 2,5 cm, escolhe cores térmicas e constrói LOD de superfícies. |

O EKF de movimento proposto no primeiro plano **não foi implementado**. O filtro da base é Kalman escalar aplicado **depois** da trilateração; ele não atua quando a posição é recusada. Um Kalman aplicado indiscriminadamente a pontos LiDAR sucessivos misturaria objetos distintos. A agregação por voxel agora suaviza a posição de observações que caem na mesma célula espacial, preservando a temperatura medida e a posição global da nuvem.

## Causas prováveis da recusa

1. O antigo botão **Redefinir perfil** voltava a aplicar automaticamente correções provisórias de ensaios sem orientação controlada. Isso foi removido na revisão do aplicativo: sem perfil medido, os valores ficam brutos e nenhuma pose automática é aplicada.
2. Com 175 mm na base e lados de 100 mm, a abertura do triângulo é pequena comparada aos alcances de 1 a 5 m. O ruído de poucos centímetros pode virar erro de posição de metros. As três antenas são coplanares e deixam uma solução espelhada. O limite de 0,5 m do app é deliberadamente exigente; um resíduo pequeno entre esferas não garante posição correta.

   Na orientação frontal, o próprio modelo geométrico do app amplifica 5 cm de erro por alcance para cerca de **1,33 m a 1 m**, **2,66 m a 2 m** e **5,31 m a 4 m**. Portanto, uma única trinca da placa compacta dificilmente passa no limite de 0,5 m, mesmo com calibração de dois pontos. Veja o cálculo e as hipóteses no [diagnóstico registrado](../diagnostics/20260926/analise.md).
3. A marcação manual AR mostrou o eixo e funcionou no ensaio do autor. A ausência do eixo ocorre somente quando se usa a pose automática UWB recusada; não é uma falha geral da renderização.
4. Atrasos de antena individuais, orientação da PCB em relação à câmera, reflexões no quarto, metal/celular perto da antena e distâncias físicas medidas de outro ponto podem deslocar as leituras. Os [registros de 26/09](../diagnostics/20260926/analise.md) confirmam a incompatibilidade frequente, mas não separam essas causas sem uma referência imóvel conhecida.

Há também um sinal direto nas capturas: o trio **1,459 / 1,257 / 1,778 m** contém uma diferença de **0,521 m** entre os rádios da base, separados por **0,175 m**. Para uma tag e âncoras paradas, distâncias simultâneas não podem diferir mais que a separação entre as âncoras. Isso indica pelo menos uma leitura com viés/erro ou amostras colhidas em momentos distintos enquanto o celular se movia. O trio **1,416 / 1,502 / 1,406 m** passa nesse teste simples, mas ainda pode ter grande incerteza de posição. Nos 4.323 conjuntos completos da primeira sessão, 3.074 (71,1%) ultrapassaram pelo menos um limite físico de diferença; como o celular se moveu, isso não mede isoladamente o erro de um rádio.

Na nova tentativa, o aplicativo registrou dois salvamentos válidos: **1/4 m** e, depois, **0,5/1,5 m**. O segundo substituiu o primeiro. Após o segundo, 54 dos 58 conjuntos completos registrados violaram pelo menos um limite físico mesmo após a correção. O autor confirmou que entrou no AR somente após o primeiro perfil, quando o eixo automático continuou ausente. A última calibração ainda não foi ensaiada no AR. O relatório de diagnóstico registra os valores e as ressalvas.

## Revisão de arquitetura

- **Visualizador:** manter o ESP como responsável pelos três intercâmbios DS-TWR, integridade, tempo e diagnóstico por rádio. Medir média, dispersão e viés de cada antena antes de ajustar atrasos no firmware ou sintonizar um filtro temporal por alcance. Preservar sempre a leitura bruta junto da filtrada, se o filtro for introduzido.
- **Celular:** ser a única autoridade para transformar posição UWB para o mundo AR, porque só ele conhece a pose da câmera. Manter a opção AR fixa para o scanner parado. Usar pose UWB móvel apenas com calibração validada em distância independente e geometria suficiente. O CSV automático desta revisão permite comparar alcance bruto, alcance corrigido, perfil, estado e etapa de cada rádio sem depender de captura de tela. Para o scanner parado, registrar também as poses AR sucessivas do celular e resolver as distâncias de várias posições do telefone: esse movimento cria uma linha de base muito maior que os 175 mm da PCB. Essa hipótese precisa de dados sincronizados e ensaio físico antes de comandar o eixo.
- **Scanner:** continuar projetando LiDAR e associando temperatura no ESP32-S3. Validar pan, inclinação, extrínsecos térmicos e relógios com alvos físicos. Filtrar retornos inválidos antes da rede e reduzir redundância espacial na nuvem; não atribuir uma trajetória de Kalman a feixes que observam superfícies diferentes.
- **Movimento:** o protocolo atual tem relógios locais independentes, sem interpolação entre a pose UWB e o instante de cada ponto. Isso precisa ser resolvido antes de considerar uma nuvem móvel precisa. Uma tag também não informa yaw do scanner.

## Próximo ensaio verificável

1. Com o perfil 0,5/1,5 m já salvo no APK instalado, colocar scanner e visualizador imóveis, de frente um para o outro e com linha de visão. Medir um terceiro ponto independente, por exemplo 2,0 m, da tag ao ponto médio entre as duas antenas da base. Manter a orientação e registrar 20–30 s sem movimento ou motores. O terceiro ponto verifica a transferência da calibração.
2. O aplicativo grava automaticamente cada diagnóstico USB em `Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/UwbDiagnostics/`. Após o ensaio, reconectar o celular ao PC permite extrair os CSVs. Comparar as três médias, dispersões e diferenças pareadas com 100/100/175 mm, considerando a posição real da tag.
3. Corrigir primeiro falhas de TWR e desvios constantes por antena. Ajustar atraso de antena somente após ensaio conhecido e repetir o terceiro ponto. Então escolher ruído de medição/processo para um filtro de alcance no ESP da base e verificar se ele reduz variância sem atrasar movimentos.
4. Para rastrear em 3D com precisão, ampliar significativamente a separação das âncoras ou aproveitar vários pontos de vista do telefone com pose AR conhecida, além de resolver o lado do plano e a orientação por geometria adicional ou referência visual. O filtro não substitui essa informação física.

O procedimento DS-TWR segue a fórmula de tempo de voo descrita na [APS013 da Decawave](https://forum.qorvo.com/uploads/short-url/x34DrF7EW5fQP9wY3aNESqPKz8z.pdf). A revisão de atraso de antena é discutida no datasheet DWM1000 v1.6, seção 2.1.3, fornecido pelo autor.

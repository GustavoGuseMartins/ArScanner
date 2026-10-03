# Plano de posicionamento e visualização sem alterar a placa

## Escopo desta entrega

O scanner mantém a câmera RGB desabilitada: ela compartilha GPIO 4/5 com o UWB na placa atual. O celular fornece a trajetória visual e inercial por ARCore; o scanner fornece LiDAR, pan, IMU, térmica e UWB. A arquitetura usa ideias de fusão temporal e estimativa de pose de VIRAL/LIRO, adaptadas aos sensores disponíveis. Não chamar a solução de VIRAL SLAM completo sem reproduzir seus sensores, calibração e avaliação.

Antes de implementar as alterações de comportamento, validar os referenciais e registrar uma linha de base com o equipamento atual. Os prints do celular ajudam a comparar a visualização, mas não medem o erro de posição em centímetros; isso exige alvos físicos medidos.

## Etapa 1 — corrigir orientação térmica

1. Com o scanner parado, pan fixo e alvo quente pequeno, registrar o lado físico observado, a prévia 24 × 32, o ponto LiDAR e a cor na nuvem. Repetir nos quatro quadrantes e após giros conhecidos do pan.
2. Identificar se a troca de lado vem do mapeamento da MLX90640, do referencial LiDAR/pan, da prévia girada ou da visualização de superfícies traseiras. Corrigir apenas a transformação identificada.
3. Aceitar quando o alvo quente aparecer no mesmo lado físico em prévia e nuvem, em pelo menos duas orientações do pan.

## Etapa 2 — limpar os pontos mantendo o LOD

1. Reduzir o **diâmetro renderizado do ponto individual de 0,025 m para 0,00625 m**: 6,25 mm, exatamente um quarto do tamanho atual. Aplicar tanto no valor padrão do componente quanto no valor serializado da cena ativa; atualizar qualquer criador de cena que ainda configure 25 mm.
2. Manter a grade de voxelização em 0,025 m inicialmente. O tamanho visual do ponto e a resolução de armazenamento são parâmetros distintos; mudar ambos confundiria a avaliação e aumentaria a quantidade de pontos.
3. **Manter o LOD ligado e a junção de pontos próximos.** Quando um grupo for suficientemente denso, coplanar e termicamente compatível, ele continua virando uma superfície; os pontos individuais cobertos por ela ficam ocultos. Pontos isolados, bordas, lacunas e variações térmicas continuam como pontos pequenos.
4. Revisar os limites da junção: a implementação atual usa células de 16 cm e 32 cm à distância. Acrescentar uma checagem de vizinhança/maior separação entre amostras e limitar a extensão do polígono, para que um grupo esparso no mesmo bloco não feche um vão real. Ajustar tamanho da célula e distância de ativação com os dados capturados, sem desligar a fusão.
5. Comparar no mesmo trecho e na mesma distância de observação: pontos pequenos com LOD, pontos pequenos sem LOD apenas como diagnóstico e visualização anterior. Registrar tempo de quadro, pontos cobertos, polígonos, continuidade de superfícies e preservação de bordas.

## Etapa 3 — medir e estabilizar a pose

1. Definir um referencial único para mundo AR, celular/antenas, eixo da base, cabeça/pan, LiDAR, tag UWB e IMU. Medir os deslocamentos rígidos e conferir sinais e sentidos de rotação. Preservar o alinhamento de yaw já observado como correto; não assumir que uma tag UWB determina yaw sozinha.
2. Associar leituras UWB e pontos LiDAR ao instante de aquisição, estimando o deslocamento entre os relógios do celular, base e scanner. Interpolar a pose AR no tempo da leitura, em vez de usar só a pose no recebimento da mensagem.
3. Fundir a trajetória ARCore do celular, os alcances UWB, o plano/altura de apoio e a orientação relativa de pan/IMU. Rejeitar medições UWB incoerentes e manter uma estimativa de incerteza. Usar vários pontos de vista do celular para melhorar a geometria da solução.
4. Para o scanner parado, fixar a pose do eixo aceita enquanto o pan gira. Registrar a pose da cabeça no instante de cada ponto. Para movimentos da base, só liberar a captura quando existir pose temporal confiável; caso contrário, pausar novos pontos e preservar a nuvem já capturada.
5. Manter uma referência de yaw observável pela câmera do **celular** ou pelo alinhamento manual existente. Automatizar yaw apenas quando houver evidência visual suficiente da direção física do scanner; caso contrário, informar que falta orientação e oferecer o controle manual.

## Ordem e validação nas duas semanas

| Prioridade | Entrega verificável |
| --- | --- |
| 1 | Teste térmico reproduzível e correção do lado mostrado. |
| 2 | Pontos individuais de 6,25 mm, LOD de junção ativo e sem pontes sobre lacunas óbvias. |
| 3 | Registro sincronizado de AR/UWB/pan/LiDAR e métricas de latência/rejeição. |
| 4 | Pose estável para base parada, comparada com posição/altura medidas fisicamente em várias sessões. |
| 5 | Atualização temporal por ponto e captura em movimento somente se o erro medido permitir. |

O ensaio de pose deve medir erro horizontal, vertical e angular em posições conhecidas, inclusive após reiniciar o aplicativo, girar o celular e deslocar a base. O ensaio visual deve ser feito no próprio celular com a mesma parede/objeto e distância. Registrar os resultados antes de definir uma meta numérica de precisão ou afirmar que o modo de scanner móvel está pronto.

## Arquivos principais previstos

- Visualização: `Assets/Scripts/Rendering/ThermalPointCloudRenderer.cs`, `SurfaceLodBuilder.cs`, `Assets/Scenes/CenaViewer.unity`, `Assets/Editor/SceneSetupHelper.cs`.
- Pose e telemetria: `Assets/Scripts/Spatial/UwbAnchorManager.cs`, `UwbInstantPoseEstimator.cs`, `UwbArMultiviewEstimator.cs`, `Assets/Scripts/Network/PointCloudTcpReceiver.cs`, `UwbDataReceiver.cs`.
- Térmica e geometria: `firmware/scanner/include/scan_geometry.h`, `firmware/scanner/src/thermal_mlx90640.cpp` e a prévia térmica no aplicativo.

## Referências

- [VIRAL: Visual-Inertial-Ranging-Lidar Sensor Fusion](https://arxiv.org/abs/2105.03296).
- [LIRO: Lidar-Inertial-Ranging Odometry](https://arxiv.org/abs/2010.13072).
- [ARCore Depth para Unity](https://developers.google.com/ar/develop/unity-arf/depth/developer-guide).

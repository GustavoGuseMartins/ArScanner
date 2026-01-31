AR Scanner

Sistema de Realidade Aumentada que utiliza dois dispositivos móveis conectados via Wi-Fi para realizar o escaneamento e visualização de superfícies em tempo real.

O sistema opera com uma arquitetura Mestre-Escravo (Scanner-Viewer):

    O Scanner mapeia o ambiente físico usando ARCore e Raycasting, gerando nuvens de pontos.

    O Viewer recebe esses dados via protocolo UDP de baixa latência e renderiza a malha 3D sobre o mundo real, permitindo visualizar o que o outro celular está escaneando.

Funcionalidades

    Comunicação UDP Otimizada: Transmissão de pacotes customizados (Posição + Rotação) respeitando o limite de MTU (Maximum Transmission Unit) para evitar fragmentação de pacotes na rede Wi-Fi.

    Service Discovery (Broadcast): Sistema de pareamento automático. Os dispositivos se encontram na rede sem necessidade de digitar IPs manualmente.

    Rendering "Sandwich": Técnica de shader duplo (Face Sólida + Face Translúcida) para garantir visibilidade dos pontos escaneados de qualquer ângulo.

    Correção de Drift: Sistema de ancoragem (WorldRoot) para mitigar a deriva de odometria em dispositivos mais antigos.

Requisitos de Hardware

O projeto foi desenvolvido e validado no seguinte setup:
Dispositivo 1: Scanner (Emissor)

Responsável por lançar os raios (Raycast) e enviar os dados.

    Requisito Mínimo: Android 8.0, com suporte a ARCore (Google Play Services for AR).

    Sensores: Câmera, Giroscópio, Acelerômetro (Depth API recomendada).

Dispositivo 2: Viewer (Receptor)

Responsável por receber os dados e desenhar os triângulos no espaço.

    Requisito Mínimo: Android 8.0, com suporte a ARCore.

    Nota: Dispositivos mais antigos sofrem muito drift de odometria.

Rede

    Roteador Wi-Fi local (5GHz recomendado) OU Hotspot móvel criado por um dos celulares.

    Ambos os dispositivos devem estar na mesma sub-rede.

Requisitos de Software

    Unity Engine: Versão 6000.0.x (Unity 6) ou superior.

    Render Pipeline: URP (Universal Render Pipeline).

    Pacotes:

        AR Foundation

        Google ARCore XR Plugin

        XR Plugin Management

Como Compilar (Build Guide)

Como o projeto utiliza a mesma base de código para dois comportamentos distintos, é necessário configurar o Build Settings corretamente para cada dispositivo.
Para gerar o APK do SCANNER (S23+)

    Vá em File > Build Settings.

    Na lista Scenes In Build, marque apenas estas cenas (nesta ordem):

        MenuScanner (Índice 0)

        CenaScanner (Índice 1)

    Desmarque as cenas do Viewer.

    Clique em Build.

Para gerar o APK do VIEWER (S9+)

    Vá em File > Build Settings.

    Na lista Scenes In Build, marque apenas estas cenas (nesta ordem):

        MenuViewer (Índice 0)

        CenaViewer (Índice 1)

    Desmarque as cenas do Scanner.

    Clique em Build.

Como Usar

    Conecte ambos os celulares na mesma rede Wi-Fi.

    Abra o aplicativo Scanner no S23 e o Viewer no S9.

    Na tela inicial, aguarde a mensagem "Procurando...".

    O sistema de Broadcast irá conectar os dispositivos automaticamente. Quando o texto mudar para "Encontrado! IP: [Endereço]", o botão INICIAR será habilitado.

    Clique em INICIAR em ambos os aparelhos.

    Aponte o Scanner (S23) para uma parede ou chão.

    Olhe através do Viewer (S9) para ver a superfície sendo marcada virtualmente em tempo real.

Solução de Problemas (Troubleshooting)

Problema: Mensagem "Procurando..." eterna

    Causa Provável: O roteador pode estar bloqueando pacotes de Broadcast.

    Solução: Tente usar o Hotspot do S23 para conectar o S9 diretamente.

Problema: Pontos não aparecem

    Causa Provável: O tamanho do pacote UDP excedeu o MTU da rede.

    Solução: Reduza a variável PONTOS_POR_PACOTE no script ScannerSender.cs para 15 ou 20.

Problema: Pontos aparecem "deitados" na parede

    Causa Provável: O envio da rotação (Quaternion) está falhando ou incorreto.

    Solução: Verifique se o Prefab do ponto tem o eixo Z alinhado corretamente na Unity.

Problema: Mundo saindo do lugar (Drift)

    Causa Provável: O ambiente tem pouca textura ou iluminação ruim, dificultando o rastreamento do ARCore.

    Solução: Adicione objetos físicos no chão para ajudar o rastreamento visual do S9.

Licença

Este é um projeto pessoal com intuito de aprofundar o conhecimento em bibliotecas de realidade aumentada e na Unity.

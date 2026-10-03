# Auditoria das funções do aplicativo — 30/09/2026

Escopo: entradas do menu, HUD de captura, controlador e chamadas públicas relacionadas. A revisão separa funções operacionais de calibração, diagnóstico e experimentos. Não remove algoritmos necessários à aquisição nem perfis de calibração existentes. Os nomes abaixo correspondem ao código revisado; a execução em Unity e o ensaio físico são feitos na integração.

## Fluxo operacional

O painel normal contém conexões, zero do pan quando necessário, fixação da origem, direção manual explícita, início/pausa, limpeza/exportação e imagem térmica. O painel de diagnóstico começa fechado, com subseções também fechadas. Ao fixar a posição, suas instruções e alternativas recolhem para uma ação de correção. A cruz é desenhada no centro real da tela, usado pelos raycasts AR, inclusive quando a proporção da tela difere de 1280×720.

**Três referências diferentes:** confirmar o **zero do pan** salva no scanner o contador correspondente à cabeça alinhada à frente física escolhida da base; retornar ao zero usa esse contador. **Definir direção no AR** orienta a base na sessão AR atual a partir de um ponto escolhido pelo operador. O ajuste de **montagem LiDAR→base de 200°** é um extrínseco local e não substitui nenhuma das duas operações. Não é exigida uma marca impressa nem um marcador visual adicional.

| Entrada / controle | Caller / destino | Utilidade e decisão |
|---|---|---|
| Reconectar scanner | `ArScannerHUD.DrawConnections` → `PointCloudTcpReceiver.ConnectToScanner` | Mantido no painel de conexão quando desconectado. O receiver evita sessões duplicadas. |
| Autorizar USB novamente | HUD/menu → `UwbDataReceiver.RequestUsbPermissionAgain` | Mantido quando faltam dados; recupera conexão/permissão USB. Não exige que a origem manual dependa de UWB. |
| Confirmar zero do pan | `DrawPanReference` → `ConfirmPanZeroReference` | Mantido no fluxo normal somente quando falta a referência física em firmware v8+. Só fica disponível parado; aguarda o scanner confirmar. |
| Retornar cabeça ao zero salvo / relativo | `DrawPanReference` → `SendParkPan` | Mantido quando o pan está fora do zero. O rótulo distingue referência física conhecida de firmware legado relativo. Não recalibra yaw AR. |
| Cancelar retorno / parar | `DrawPanReference` → `SendStopScan` | Mantido durante o retorno; operação de parada necessária. |
| Fixar eixo UWB estimado | `DrawPositionControls` → `TryConfirmPoseCandidate` | Mantido: somente uma solução que passou geometria, altura e novos ciclos pode ser fixada. O botão não força a aceitação de um candidato instável. |
| Marcar eixo no apoio pelo AR | `DrawPositionControls` → `PlacePreviewAtScreenCenter` | Mantido como alternativa explícita e independente de reconhecimento da tampa. A instrução identifica o ponto: superfície do apoio diretamente abaixo do eixo de giro. |
| Corrigir posição do eixo | `DrawPositionControls` | Abre as ações de correção, recolhidas depois que a origem foi fixada. Evita repetir instruções completas durante a captura. |
| Refazer posição UWB | `DrawPositionControls` → `ResumeAutomaticUwb` | Mantido na correção de uma origem UWB fixada; limpa histórico/orientação para uma localização nova. Não repina silenciosamente a origem anterior. |
| Scanner parado: voltar à localização | `DrawPositionControls` → `SetScannerStationary(true)` | Recupera o fluxo normal caso um teste de movimento tenha sido ativado. |
| Definir / corrigir direção no AR | `DrawDirectionControls` → `AlignPreviewForwardAtScreenCenter` | Mantido e visível após fixar o eixo UWB ou marcar o eixo no apoio pelo AR. O operador escolhe no apoio um ponto livre 30–50 cm à frente do eixo, no sentido físico da frente do LiDAR. Não fica subordinado ao reconhecimento da tampa. |
| Iniciar / retomar captura | `DrawAcquisitionControls` → `SendStartScan` | Mantido. Exige o gate do manager e referência utilizável do pan; envio do comando significa início pendente até a confirmação do scanner. |
| Cancelar início / parar | `DrawAcquisitionControls` → `SendStopScan` | Mantido enquanto o início está pendente. |
| Pausar captura | `DrawAcquisitionControls` → `SendStopScan` | Mantido; não redefine zero físico nem direção AR. |
| Exportar PLY | `DrawCloudAndThermalControls` → `ExportToPLY` | Mantido; exporta a nuvem adquirida. O resultado vazio é informado. |
| Limpar nuvem | `DrawCloudAndThermalControls` → `ClearPointCloud` | Mantido. Apaga pontos, não equivale a recalibrar pan nem direção. |
| Colorir pontos com temperatura válida | `DrawCloudAndThermalControls` → `showThermalColors` | Mantido como opção de visualização. Geometria sem medição térmica continua disponível. |
| Ver / fechar imagem térmica | `DrawCloudAndThermalControls`, `DrawThermalPreview` → `SetCameraPreview(2/0)` | Mantido como visualização térmica principal. A imagem e suas temperaturas só aparecem com quadro completo do sensor com até 1 s e textura HTTP recebida há até 1,5 s; sem isso, aparecem estado e idade, evitando apresentar uma textura antiga como câmera ativa. |
| Diagnóstico e ajustes | `DrawMainControls` | Mantido como porta única para funções técnicas; fechado por padrão. |
| Voltar ao menu | `DrawMainControls` → `SendStopScan`, `SceneManager.LoadScene` | Mantido com solicitação de parada antes da navegação. |

## Diagnóstico, montagem e experimentos

| Entrada / controle | Caller / destino | Utilidade e decisão |
|---|---|---|
| Atualizar geometria | `DrawDiagnostics` → `RequestGeometry` | Mantido no diagnóstico: confere a montagem publicada pelo firmware; não é etapa obrigatória de cada scan. |
| Salvar amostras do scanner (CSV) | `DrawDiagnostics` → `DownloadScanCsv` | Mantido e renomeado para distinguir do PLY e dos CSVs de diagnóstico UWB automáticos. |
| Registro da sessão do scanner (JSONL) | `DrawDiagnostics` → `ScannerDiagnosticPath` | Mostra o nome do registro automático de status e comandos, que continua disponível mesmo sem base UWB. |
| Sensores e câmeras | `DrawDiagnostics` → `DrawSensorDiagnostics` | Subpainel oculto. Contém RPM, passos/referência, MPU/I2C, tag e estado térmico. |
| Ver / fechar RGB do scanner | `DrawSensorDiagnostics` → `SetCameraPreview(1/0)` | Mantido apenas no diagnóstico e oferecido quando `rgbReady`. A RGB indisponível deixa de ocupar o fluxo normal. |
| Calibrar orientação térmica | `DrawSensorDiagnostics` | Subpainel oculto, mas preservado; a calibração válida da montagem continua acessível. |
| Inverter lado da câmera térmica | Subpainel térmico → `RequestThermalOrientation(profile ^ 1)` | Mantido, parado; confere correspondência térmica com alvo quente. O receiver limpa a nuvem após alterar a orientação. |
| Espelhar imagem térmica | Subpainel térmico → `RequestThermalOrientation(profile ^ 2)` | Mantido, parado; corrige a orientação da montagem e não mede yaw AR. |
| Reconfirmar zero do pan | `DrawSensorDiagnostics` → `ConfirmPanZeroReference` | Movido para diagnóstico quando já existe um zero. Reduz risco de tratar a operação como ajuste cotidiano de orientação AR. |
| Altura do eixo sobre o apoio | `DrawMountingControls` → `previewOriginHeight` | Mantida em montagem. Corrige uma dimensão física; ao alterar, limpa nuvem e exige nova orientação/localização quando necessário. |
| Girar referência da placa USB | `DrawMountingControls` → `RotateBoardInPhone90` | Mantido em montagem; representa a orientação física da placa no celular e preserva a configuração salva. |
| Yaw/pitch/roll de montagem | `DrawMountingControls` → offsets do renderer | Mantidos como extrínsecos, ocultos. O yaw de montagem deixa de ser apresentado como o yaw da base no AR. |
| Inverter vertical LiDAR | `DrawMountingControls` → `invertVerticalLidar` | Mantido como teste da hipótese chão/teto, com limpeza da nuvem ao trocar. |
| Restaurar montagem | `DrawMountingControls` | Mantido; restaura yaw **200°**, pitch/roll 0°. O antigo “zerar yaw” eliminava um extrínseco indispensável e foi corrigido. |
| Unir pontos no plano (LOD) | `DrawMountingControls` → `enableSurfaceLod` | Mantido como exibição; o núcleo de reconstrução não foi alterado. |
| Mostrar indicador do eixo | `DrawMountingControls` → `showScannerAxes` | Mantido. Sem direção confirmada, o renderer só mostra a vertical, sem X/Z arbitrários. |
| Opacidade / escala térmica / contraste | `DrawMountingControls` → propriedades do renderer | Mantidos como ajustes de apresentação; não alteram medições de temperatura. |
| Recomeçar localização UWB automática | `DrawExperiments` → `ResumeAutomaticUwb` | Preservado fora do fluxo normal para retornar do modo manual ou reiniciar uma investigação. |
| Testar deslocamento livre / encerrar | `DrawExperiments` → `SetScannerStationary` | Movido para bancada. O acompanhamento sem sincronismo comprovado não deve oferecer captura. |
| Testar direção automática pelo apoio | `DrawExperiments` → `autoAlignHeading` + `autoSupportHeadingFallback` | Preservado como experimento explícito; direção manual permanece a alternativa normal. |
| Testar reconhecimento da tampa laranja | `DrawExperiments` → `SetNaturalCoverRecognition` | Preservado, **desligado por padrão**. Cor/modelo físico fornecem hipótese experimental e não identidade/precisão certificadas. Não é requisito para fixar origem ou definir direção. |
| Bancada 2D/3D: giro externo | `DrawExperiments` → `SendPanEnabled` | Movido para bancada; evita confundir “scanner parado no ambiente” com “pan desativado”. |
| Velocidade do LiDAR (%) e pan (RPM) | `DrawExperiments` → `SendSetLidarSpeed`, `SendSetSpeed` | Sliders preservados na bancada. Presets redundantes foram retirados. Os antigos rótulos “1.0x/2.5x/5.0x” enviavam **RPM absolutos**, não multiplicadores; agora a unidade está explícita. |
| Setorial 180° / contínuo 360° | `DrawExperiments` → `SendSetMode` | Preservado na bancada como escolha de aquisição; não muda posição nem yaw AR. |
| Simulação no editor | `DrawExperiments` → `ArScannerController.SetSimulationMode` | Mantida só no editor. Usa o controlador para sincronizar receiver, UWB, pose, simulação e limpeza; não alterna apenas o gerador de pontos isoladamente. |

## Menu, referências públicas e funções sem botão atual

| Função / entrada | Caller real | Decisão |
|---|---|---|
| Abrir scanner AR / `IniciarComHardware` | Botão do menu criado em `MenuViewer.BuildMenu` | Mantido; entrada com scanner Wi-Fi disponível. Ausência de alcances UWB não impede marcação AR manual; captura continua bloqueada até origem, pan e direção válidos. |
| `CarregarCenaDoViewer` | Referência serializada em `Assets/Scenes/MenuViewer.unity` e wrapper público | Mantido por compatibilidade da cena. O Canvas legado é ocultado pelo menu, evitando botão duplicado. |
| Autorizar USB no menu | `usbRetryButton.onClick` | Mantido, aparece quando a base não está online. |
| Abrir/fechar calibração UWB | `ToggleCalibration` pelo botão do menu | Mantido recolhido: calibração de alcance válida e útil, separada da localização/orientação AR. |
| Capturar perto / longe | `CaptureRanges(true/false)` pelo menu | Mantido: coleta 20 ciclos recentes com dispositivos imóveis para cada distância física conhecida. |
| Salvar calibração | `SaveCalibration` pelo menu → `UwbRangeCalibrationProfile` | Mantido: modelo de escala/offset em dois pontos, com validações e persistência. Não equivale a confirmar posição 3D nem yaw. |
| Redefinir perfil | `ResetCalibration` pelo menu | Mantido; remove somente o perfil de alcance. Não é limpeza de nuvem nem reset de pan. |
| `IniciarModoSimulacao` | API pública de desenvolvimento; sem botão principal do menu | Mantida, sem duplicar fluxo operacional. |
| `CalibrateUwbRangeAtPreview` | Nenhum caller C# ou referência de cena encontrados | Removido: ajuste antigo por marcação sem uso; a calibração válida de alcance em duas distâncias continua disponível no menu. |
| `CalibrateUwbMotionAtPreview`, `SetUwbMotionTest` | Nenhum caller C# ou referência de cena encontrados | Removidos: entradas de bancada sem uso e sem garantia de captura móvel. Os controles de experimento expostos e a comparação de alcance permanecem no diagnóstico. |
| `ResetOriginToCurrent`, `SetYawOffset` | Nenhum caller C# ou referência de cena encontrados | Removidos: entradas antigas sem uso que duplicavam os controles explícitos de eixo e direção. |
| `RotatePitch90`, `RotateYaw90` | Nenhum caller C# ou referência de cena encontrados | Removidos: atalhos redundantes sem uso. Sliders de montagem preservam os ajustes necessários. |
| `UpdateDroneAttitude` | `PointCloudSimulator` | Útil para simulação; mantido, não removido por ausência de botão. |

## Núcleo preservado

Conexão/reconexão TCP, controle e heartbeat, associação temporal UWB↔AR, calibração de alcance, observação do apoio, trilateração multivista, validação de candidatos, ancoragem AR manual, gate de aquisição, parsing de pontos, geometria/térmica, voxelização, LOD, exportação e navegação continuam necessários. A organização do HUD não altera seus algoritmos. Mudanças de firmware e do manager têm suas próprias validações na integração.

O estado térmico de diagnóstico diferencia inicialização, montagem das duas subpáginas, quadro completo recente, quadro antigo e falha de leitura. Um sensor “iniciado” não é tratado automaticamente como quadro térmico válido. A interface evita sugerir alimentação, ligação elétrica ou defeito de hardware apenas a partir de um contador de erro.

O modo parcial v10 acrescenta informação de qualidade, sem novos controles: imagem com máscara explícita, lacunas visíveis e aviso de pixels sem calibração excluídos. A contagem exibida vem do mesmo snapshot da imagem; os novos campos de qualidade são preservados no JSONL. O fluxo de fixar eixo e definir direção continua separado dessa limitação térmica.

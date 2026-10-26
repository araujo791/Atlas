# Imported NTLite removal preset

Only the 80 RemoveComponents entries were imported. Registry edits, unattended
setup, commands, feature enables and other preset settings were not imported.

Source XML SHA-256: 9f1c3ca8eb8976b39738c94409c554d33c21d57c008a5d2059f06f679f4cd907

Entries: 80. Newly mapped AppX entries: 13; already covered
AppX entries: 3; pending native/unknown component mappings: 64.
New package patterns: 14.

The executable subset is Configuration/atlas/ntlite-appx.yml, included
inside the existing AppX task after its inventory snapshot and before
deprovisioning cleanup. Both tasks run on Windows/OOBE, not during offline
ISO mastering. Package identities and removability still require inventory
and execution against build 26300.9807. A mapping does not prove removal.
System apps may reject uninstallation; inspect all action logs.

This preset requests removals affecting accessibility, Hello, security,
recovery, backup and servicing. Native component removals remain pending;
they are not replaced by file deletions or service disabling. SecHealthUI
removal is conditional on the existing defender-disable choice.

No full NTLite-equivalent conversion or offline image reduction is claimed.
Regenerate with: python3 scripts/import-ntlite-removals.py

| NTLite ID | Label | Status | Mapping / next step |
| --- | --- | --- | --- |
| accessibility_cursors | Ponteiros do Mouse (Ferramentas de Acessibilidade) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| accessibility_magnifier | Lupa | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| accessibility_narrator | Narrador | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| asimov | Cliente de Telemetria | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| backup | Backup e Restauração | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| bingsearch | Bing Search (InboxApp) | Existing AppX rule (Windows/OOBE; untested) | Microsoft.BingSearch* |
| calculator | calculator | AppX actions added (Windows/OOBE; untested) | Microsoft.WindowsCalculator* |
| charmap | Mapa de Caracteres | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| clouddownload | Recuperação do Windows (WinRE) - Download da nuvem | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| consentux | Consentimento UX | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| copilotpwa | Windows Copilot (InboxApp) | Existing AppX rule (Windows/OOBE; untested) | Microsoft.Copilot* |
| datacenterbridging | Data Center Bridging (DCB) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| directml | Aprendizado de Máquina Direto (DirectML) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| diskquota | Cota do Disco | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| diskquota32 | Cota do Disco - 32 bits | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| driver_c_fdc.inf | driver_c_fdc.inf | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| driver_c_floppydisk.inf | driver_c_floppydisk.inf | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| driver_fdc.inf | driver_fdc.inf | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| driver_flpydisk.inf | driver_flpydisk.inf | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| driver_helloface.inf | driver_helloface.inf | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| easeofaccessthemes | Temas de Facilidade de Acesso | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| enterprisedataprotection | Proteção de Dados Empresariais (EDP/WIP) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| errorreporting | Relatório de Erros do Windows | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| eudcedit | Editor de Caracteres Particulares | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| facerecognition | Reconhecimento de Face | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| flipgridpwa | Flipgrid PWA (InboxApp) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| gameexplorer | Explorador de Jogos | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| guardedhost | Anfitrião Vigiado | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| hwsupport_floppy | Disquete | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| livecaptions | Legendas ao vivo | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| manifestbackup | Backup de Manifest (WinSxS\Backup) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| mapcontrol | Controle de Mapa | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| messagingsvc | Serviço de Mensagens | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| microsoft.asynctextservice | AsyncTextService | AppX actions added (Windows/OOBE; untested) | Microsoft.AsyncTextService* |
| microsoft.bioenrollment | Configuração do Windows Hello | AppX actions added (Windows/OOBE; untested) | Microsoft.BioEnrollment* |
| microsoft.ecapp | Controle com os Olhos | AppX actions added (Windows/OOBE; untested) | Microsoft.ECApp* |
| microsoft.sechealthui | Windows Defender | AppX actions added (Windows/OOBE; untested) | Microsoft.SecHealthUI*, Microsoft.Windows.SecHealthUI*; only with the existing Disable Defender option |
| microsoft.services.store.engagement | Microsoft Engagement Framework | AppX actions added (Windows/OOBE; untested) | Microsoft.Services.Store.Engagement* |
| microsoft.windows.apprep.chxapp | SmartScreen | AppX actions added (Windows/OOBE; untested) | Microsoft.Windows.Apprep.ChxApp* |
| microsoft.windows.narratorquickstart | Narrador | AppX actions added (Windows/OOBE; untested) | Microsoft.Windows.NarratorQuickStart* |
| microsoft.windows.parentalcontrols | Microsoft Family Safety | AppX actions added (Windows/OOBE; untested) | Microsoft.Windows.ParentalControls* |
| microsoft.windows.secureassessmentbrowser | Fazer um Teste | AppX actions added (Windows/OOBE; untested) | Microsoft.Windows.SecureAssessmentBrowser* |
| microsoft.xboxgamecallableui | Xbox Game UI | AppX actions added (Windows/OOBE; untested) | Microsoft.XboxGameCallableUI* |
| microsoftwindows.client.aix | Windows Copilot (Pacote de Experiência de Recursos do Windows) | AppX actions added (Windows/OOBE; untested) | MicrosoftWindows.Client.AIX* |
| mobilepc | mobilepc | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| mobilepc_networkprojection | Projeção de Rede | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| netqos | Agendador de Pacotes Quality of Service (QoS) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| nettopology | Protocolo de Descoberta de Topologia na Camada do Link (LLTD) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| outlookpwa | Outlook PWA (InboxApp) | Existing AppX rule (Windows/OOBE; untested) | Microsoft.OutlookForWindows* |
| payments | Pagamentos | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| penservice | Serviço de Caneta | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| proquota | Cota do perfil | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| retaildemo | Conteúdo de Demonstração para Varejo | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| rotationmanager | Gerenciador de Rotação Automática | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| securestartup | Criptografia de Unidade BitLocker | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| sleepstudy | Estudo do Sono | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| speechapi | API de Fala | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| speechcommon | Reconhecimento de Fala | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| speechtts | Fala TTS | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| spellchecking | Verificação Ortográfica | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| startexperiencesapp | Start Experiences (InboxApp) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| stepsrecorder | Gravador de Passos | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| sustainabilityservice | Serviço de Sustentabilidade | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| systemreset | Redefinir Sistema para Windows | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| systemrestore | Restauração do Sistema | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| tabletpc | Tablet PC | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| textprediction | Previsão de texto | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| voiceaccess | Acesso por voz | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| vss | Cópia de Sombra de Volume | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| waasassessment | Serviço médico do Windows Update | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| wallet | Serviço Wallet | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| webcamexperience | Experiência da Webcam | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| webthreatdefense | Serviço de Defesa contra Ameaças da Web | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| widgetsplatformruntimeapp | Widgets Platform Runtime (InboxApp) | AppX actions added (Windows/OOBE; untested) | Microsoft.WidgetsPlatformRuntime* |
| winai | Aprendizado de Máquina do Windows AI | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| windowstogo | Windows To Go | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| winre | Recuperação do Windows (WinRE) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| winsat | Ferramenta de Avaliação de Sistema do Windows (WinSAT) | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| xbox | Xbox - Núcleo | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |
| zipfolder | Pasta ZIP & CAB | Pending: no verified component mapping | Requires image inventory and a component-specific implementation |

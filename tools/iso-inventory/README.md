# ISO Inventory 0.2.0 — catálogo com seleção

Executável portátil para Windows 10/11 x64, com runtime .NET incluído.
Abra releases/ISOInventory-0.2.0-win-x64.exe. A elevação de administrador é
necessária para as montagens e consultas offline do Windows.

## Usar

1. Selecione ISO, WIM, ESD ou a raiz de uma pasta Windows offline.
2. Pasta ou imagem com uma edição inicia automaticamente. Para várias edições,
   escolha uma na janela de seleção.
3. Expanda os grupos em Componentes da imagem. Clique no nome para ler estado,
   evidências e dependências declaradas no painel à direita.
4. Marque os itens para futura remoção. Todos começam desmarcados. Marcar um
   grupo seleciona seus itens no filtro atual, inclusive páginas não abertas.
   A seleção é preservada ao mudar a busca.
5. Exportar seleção / JSON salva o catálogo completo, SelectionIntent e
   SelectedCatalogIds. As marcações são planejamento; não executam remoções.

A aba Arquivos vinculados foi retirada. A interface agora usa uma árvore de
componentes com caixas de seleção. Grupos grandes têm páginas expansíveis de
250 itens; não existe o antigo corte visual de 5.000 resultados. O catálogo
completo é pesquisável e exportável, mesmo com grupos ainda fechados.

## Conteúdo identificado

- Componentes integrados: Defender Antivirus, Segurança do Windows, Edge,
  WebView2, WinRE e instalador do OneDrive, quando há evidências na imagem.
- Aplicativos AppX/MSIX e do sistema por manifesto; provisionamento via DISM.
- Programas tradicionais registrados no HKLM offline de 32/64 bits.
- Todos os registros de serviços e drivers no ControlSet selecionado.
- Pacotes de drivers pelo DISM /Get-Drivers /All e pelos INF em Windows/INF.
- Recursos opcionais e capabilities pelas consultas do DISM.
- Pacotes CBS: inventário DISM e fallback dos manifestos em servicing/Packages.
- Componentes WinSxS: identidades de assemblies presentes nos manifestos.
  Manifestos comprimidos/não decodificados continuam visíveis pelo identificador
  do arquivo, com esse limite indicado nos detalhes.
- Scripts em Windows/Setup/Scripts, listados sem execução e sem inventar seus
  efeitos futuros.

Cada item informa a evidência/estado: arquivo presente, apenas diretório,
registro de serviço, manifesto sem confirmação de ativação, estado DISM etc.
Uma pasta vazia ou um manifesto isolado não comprova instalação/ativação.
O estado DISM substitui o fallback do manifesto CBS quando ambos identificam o
mesmo pacote. Versões/arquiteturas diferentes de assemblies WinSxS são mantidas.

A coleta desta interface usa metadados em vez de enumerar todos os arquivos de
cada pacote/programa. O campo legado Files do JSON pode ficar vazio; não é o
catálogo de componentes. O relatório usa schema 2, com Catalog e seleção.

## Segurança e limites de interpretação

ISO é montada somente para leitura; WIM usa /ReadOnly e desmonta com /Discard.
ESD é exportado para um WIM temporário e pode exigir vários GB no TEMP. Somente
as montagens criadas pelo app são liberadas. Hives são copiados para TEMP e
somente essas cópias são abertas com KEY_READ, sem carregar o HKLM do computador.
Links/junções internos não são seguidos. Comandos encontrados na imagem não são
executados. Falhas de desmontagem preservam a pasta e registram seu caminho.

Este catálogo não reproduz o grafo proprietário de dependências/remoção do
NTLite. Nem todo item identificado é removível, e presença na imagem não implica
que ele estará ativo. Scripts, downloads, escolhas do instalador e atualizações
posteriores podem instalar conteúdo que não é determinável só por esta análise.
As marcações não são um plano executável pelo DISM nem certificam remoção segura.
Imagens SWM divididas ainda não são suportadas.

Abrir log mostra o diagnóstico local em %LOCALAPPDATA%/ISOInventory/Logs. A saída
nativa é limitada a 16.000 caracteres por stream no log, sem cortar o inventário.
Cancelamento aguarda operações nativas de montagem em andamento antes da limpeza.

## Desenvolvimento e validação

    dotnet test tools/iso-inventory/tests/Inventory.Tests/Inventory.Tests.csproj -c Release
    dotnet publish tools/iso-inventory/src/Inventory.App/Inventory.App.csproj -c Release -r win-x64 -o artifacts/iso-inventory
    dotnet run --project tools/iso-inventory/tests/VerifyAppStartup -- tools/iso-inventory/src/Inventory.App/bin/Release/net8.0-windows/win-x64/ISOInventory.dll

Os testes portáteis cobrem dados reais de fixture, leitura sem alterar a entrada,
resolução de caminhos, XML, inventários DISM, detecção de integrados, catálogo,
WinSxS, versões e precedência de estados. O verificador inspeciona o STAThread
no ponto de entrada compilado. A versão é cross-published em Linux para Windows;
a árvore interativa e as consultas nativas precisam de confirmação em Windows.
O executável é experimental e não assinado.

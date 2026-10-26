# ISO Inventory — portátil, somente leitura

Protótipo para Windows 10/11 x64. Abra o executável em releases; ele inclui o
runtime .NET e não precisa de instalação. A elevação de administrador é usada
pelo Windows para montar imagens e consultar os hives offline.

## Uso

1. Abra ISOInventory-0.1.1-win-x64.exe.
2. Selecione uma ISO, install.wim, install.esd ou uma pasta Windows offline.
3. Escolha a edição e clique em Analisar.
4. Consulte Aplicativos, Serviços e drivers, Arquivos vinculados, Componentes
   e Avisos e limites. Use a busca para filtrar a aba atual.
5. Dê duplo clique em um aplicativo/serviço para ver os arquivos associados.
   Limpar filtro volta à lista completa.
6. Salvar JSON completo exporta todos os registros. A grade mostra no máximo
   5.000 resultados de cada filtro; o JSON contém o inventário completo.

ESD é exportado para um WIM temporário antes da montagem: reserve vários GB no
disco temporário e aguarde a conclusão. Cancelamento não mata o DISM no meio de
uma montagem; ele aguarda a operação nativa terminar e faz a limpeza.

## O que é identificado

- Pacotes provisionados: consulta DISM, mesmo quando WindowsApps não é acessível.
- AppX/MSIX e apps do sistema: manifestos das pastas padrão, versões, publisher,
  dependências declaradas, arquivos do pacote e tamanhos lógicos.
- Programas clássicos: metadados de desinstalação HKLM 32/64 bits, referências a
  ícones/desinstaladores e arquivos das pastas InstallLocation declaradas.
- Serviços/drivers: ControlSet indicado pelo hive SYSTEM, início, conta,
  dependências, ImagePath e Parameters/ServiceDll.
- Recursos opcionais, capabilities e pacotes CBS: consultas de inventário DISM.
- Arquivos vinculados: presença, tamanho lógico, origem do vínculo e múltiplos
  vínculos ao mesmo caminho (por exemplo, svchost.exe compartilhado).

Os hives SOFTWARE e SYSTEM são copiados para uma pasta temporária e somente
as cópias são abertas com RegLoadAppKey/KEY_READ; não são carregados no HKLM do
computador. ISO é montada com Access ReadOnly, e WIM com /ReadOnly. A desmontagem
usa /Discard. O aplicativo não altera serviços, remove pacotes ou executa os
comandos encontrados no registro. Links e junções internos não são seguidos.

Se a imagem já estiver montada pelo usuário, a ISO não é desmontada pelo app.
Pastas Windows offline fornecidas pelo usuário não são montadas/desmontadas.
Somente as montagens e pastas temporárias criadas pelo app são limpas. Se uma
desmontagem falhar, a pasta é preservada e o local é registrado nos avisos.

## Limites

Este é um inventário, não o motor de remoção e dependências do NTLite.
InstallLocation indica uma pasta declarada, não a propriedade exclusiva dos
arquivos. Programas que apontam para raízes compartilhadas não têm essas raízes
atribuídas a eles. Paths sem aspas podem ser ambíguos e são marcados como estimados.

Tamanhos são lógicos: hard links, componentes compartilhados e compressão
impedem usá-los como previsão de espaço recuperável. Múltiplos vínculos significam
o mesmo caminho no inventário, não detecção completa de hard links/ownership CBS.
Recursos e pacotes CBS não possuem mapeamento automático completo para seus arquivos.
Apps por usuário, nomes ms-resource e descrições indiretas de DLLs podem exigir
análise adicional; nomes indiretos são mostrados sem executar DLLs da imagem.
Pode haver uma entrada de provisionamento e outra do conteúdo físico do pacote.
Imagens SWM divididas ainda não são suportadas.

Erros e falta de acesso aparecem em Avisos; inventários vazios não provam que a
imagem não contém aqueles itens. No máximo os primeiros 1.000 avisos de varredura
são retidos, e a contagem total/quantidade omitida são informadas.

## Validação desta versão

Testes portáteis cobrem o scanner real com diretórios e manifestos de teste,
caminhos offline, associação de serviços, pastas de programas, XML não confiável,
cancelamento, Unicode e parsing do inventário DISM. A entrada dos testes é
conferida para confirmar que não foi alterada.

O executável Windows é compilado e publicado a partir de Linux com targeting
Windows. A interface, RegLoadAppKey e montagem/consultas nativas do DISM ainda
precisam ser exercitadas em um Windows real. Não foi analisada a ISO 26300.9807
neste ambiente. O executável não é assinado.

A versão 0.1.1 corrige a inicialização sem STAThread, exigido pelos seletores
nativos de arquivos/pastas. Erros lançados pelos seletores também passam a ser
exibidos na janela. O build verifica o atributo no ponto de entrada compilado
com VerifyAppStartup; isso não substitui o teste interativo em Windows.

## Compilar

Requer SDK .NET 8 e acesso ao NuGet durante o build; não no uso do executável.

    dotnet test tools/iso-inventory/tests/Inventory.Tests/Inventory.Tests.csproj -c Release
    dotnet publish tools/iso-inventory/src/Inventory.App/Inventory.App.csproj -c Release -r win-x64 -o artifacts/iso-inventory

O publish gera um executável com runtime incluído e comprimido; bibliotecas
nativas do runtime são extraídas automaticamente no TEMP do Windows ao iniciar.
Os projetos não dependem do build do AME, de seus CABs ou de instalação do Atlas.

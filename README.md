# bl-mcp — MCP server para modding de Bannerlord

Servidor [MCP](https://modelcontextprotocol.io) (stdio) que dá ao Claude acesso de leitura à
**API decompilada do Mount & Blade II: Bannerlord**. Em vez de abrir o ILSpy na mão, o modelo
consulta tipos, assinaturas e código-fonte do jogo direto da conversa.

Por baixo usa o [`ICSharpCode.Decompiler`](https://github.com/icsharpcode/ILSpy) (o motor do ILSpy).
As DLLs do jogo **nunca entram no contexto** — só as respostas filtradas de cada consulta.

## Ferramentas expostas

| Tool | O que faz |
|------|-----------|
| `bl_find_type` | Busca tipos por nome (substring no full name). Comece por aqui quando não souber o nome exato. |
| `bl_type_members` | Lista métodos/propriedades/campos/eventos/tipos aninhados de um tipo, com assinaturas C#. |
| `bl_find_member` | Acha onde um método/propriedade/campo está declarado, em todas as assemblies. ("onde está `GetSkillValue`?") |
| `bl_decompile` | Decompila um tipo (ou um método) de volta pra C# — essencial antes de escrever um Harmony patch. |
| `bl_index_info` | Mostra o que foi indexado (contagem + lista de assemblies). Útil pra conferir o caminho do jogo. |

## Build

```sh
dotnet build BannerlordMcp.csproj -c Release
```

Requer .NET SDK 10+. Sem dependência do net472 — o servidor *lê* as DLLs do jogo como dado.

## O que ele indexa

No boot da **primeira tool call** (lazy, ~6s, pago uma vez por sessão do servidor), varre:

- `<jogo>\bin\Win64_Shipping_Client` (engine: `TaleWorlds.Engine`, `TaleWorlds.Library`, …)
- todos os `<jogo>\Modules\*\bin\Win64_Shipping_Client` (gameplay: `TaleWorlds.CampaignSystem`,
  `SandBox`, `StoryMode`, … **e os teus próprios mods**, FriendsLord incluso)

DLLs nativas/não-gerenciadas são puladas em silêncio.

## Configurar o caminho do jogo

Resolução, nesta ordem: variável de ambiente `BANNERLORD_DIR` → 1º argumento de linha de comando →
padrão (`C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord`).

## Registrar no Claude Code

Como é ferramenta de dev reusável entre mods, registre no escopo de **usuário** (vale em todos os projetos):

```sh
claude mcp add bannerlord -s user -- dotnet "C:\Users\eduar\source\repos\bl-mcp\bin\Release\net10.0\BannerlordMcp.dll"
```

Ou, por projeto, um `.mcp.json` na raiz do repo do mod:

```json
{
  "mcpServers": {
    "bannerlord": {
      "command": "dotnet",
      "args": ["C:\\Users\\eduar\\source\\repos\\bl-mcp\\bin\\Release\\net10.0\\BannerlordMcp.dll"]
    }
  }
}
```

## Escopo / roadmap

Hoje cobre **busca + decompile** (o "nível B"). Futuro, se valer: `bl_find_callers` (quem chama um
método — varredura de IL, pra mirar Harmony patches) e `bl_find_subclasses`.

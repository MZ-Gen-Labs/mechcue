using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
static class StrictToolArguments
{
    public static async ValueTask<CallToolResult> Invoke(RequestContext<CallToolRequestParams> context, Func<RequestContext<CallToolRequestParams>,ValueTask<CallToolResult>> next)
    {
        if(context.MatchedPrimitive is McpServerTool tool && context.Params?.Arguments is {} arguments){
            var allowed=tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);
            var unknown=arguments.Keys.Where(k=>!allowed.Contains(k)).ToArray();
            if(unknown.Length>0)return new CallToolResult{IsError=true,Content=[new TextContentBlock{Text=JsonSerializer.Serialize(new{error="unknown_tool_arguments",tool=context.Params.Name,unknownArguments=unknown,allowedArguments=allowed.Order().ToArray(),mutationStarted=false})}]};
        }
        return await next(context);
    }
}

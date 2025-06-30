// Copyright (c) Microsoft. All rights reserved.

// **Documentation**: https://devblogs.microsoft.com/semantic-kernel/integrating-model-context-protocol-tools-with-semantic-kernel-a-step-by-step-guide/

// Import packages

// dotnet add package Microsoft.Extensions.Logging --> <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.6" />
// dotnet add package Microsoft.Extensions.Logging.Console --> <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.6" />
using Microsoft.Extensions.Logging; // needed for LogLevel

// dotnet add package Microsoft.Extensions.DependencyInjection --> <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.6" />
using Microsoft.Extensions.DependencyInjection; // needed for AddLogging


// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.55.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

// dotnet add package Microsoft.SemanticKernel.Agents.Core --> <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.55.0" />
using Microsoft.SemanticKernel.Agents; // needed for ChatCompletion

// dotnet add package ModelContextProtocol --prerelease --> <PackageReference Include="ModelContextProtocol" Version="0.3.0-preview.1" />
using ModelContextProtocol.Client; // contains the IMcpClient interface

// dotnet add package Microsoft.Extensions.Hosting --> <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.6" />

using DotNetEnv;
using AIPlugins; // contains the LightsPlugin class


string projectRoot;

Console.WriteLine("Application starts");

// Get the base directory
DirectoryInfo baseDirectory = new(AppDomain.CurrentDomain.BaseDirectory);

// retrieve the project root folder
#pragma warning disable CS8602 // Dereference of a possibly null reference.
if (baseDirectory.Parent.Parent.Name == "bin")
{
    projectRoot = baseDirectory.Parent.Parent.Parent.FullName;
}
else
{
    projectRoot = baseDirectory.FullName;
}
#pragma warning restore CS8602 // Dereference of a possibly null reference.

string envFilePath = Path.Combine(projectRoot, "./../../config/credentials_my.env");
Console.WriteLine($"envFilePath: {envFilePath}");

// Load the environment variables from the .env file
Env.Load(envFilePath);

// Populate values from your OpenAI deployment
var modelId = Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME");
var endpoint = Env.GetString("AZURE_OPENAI_ENDPOINT");
var apiKey = Env.GetString("AZURE_OPENAI_API_KEY");

Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {endpoint}\nAZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {modelId}");

// Create the kernel builder with the pointer to Azure OpenAI
var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);

// Use the kernel builder to add enterprise components (for logging, in this case)
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.None));

// Build the kernel
Kernel kernel = builder.Build();


// Create an MCPClient for the GitHub server
await using IMcpClient githubMcpClient = await McpClientFactory.CreateAsync(new StdioClientTransport(new()
{
    Name = "GitHub",
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-github"],
}));

// Create an MCPClient for the Azure server
await using IMcpClient azureMcpClient = await McpClientFactory.CreateAsync(new StdioClientTransport(new()
{
    Name = "Azure",
    Command = "npx",
    Arguments = ["-y", "@azure/mcp@latest", "server", "start"],
}));


// Retrieve the list of tools available on the GitHub server
// The following code lists the tools exposed by the server and prints out each tool name and description
var github_tools = await githubMcpClient.ListToolsAsync().ConfigureAwait(false);
Console.WriteLine("\nGitHub MCP tools available:");
foreach (var tool in github_tools)
{
    Console.WriteLine($"{tool.Name}: {tool.Description}");
}


// The following code lists the tools exposed by the server and prints out each tool name and description
var azure_tools = await azureMcpClient.ListToolsAsync().ConfigureAwait(false);
Console.WriteLine("\nAzure MCP tools available:");
foreach (var tool in azure_tools)
{
    Console.WriteLine($"{tool.Name.Replace("-", "_")}: {tool.Description}");
}


// add the Lights plugin to the kernel
Microsoft.SemanticKernel.KernelPlugin lights_plugin = kernel.Plugins.AddFromType<LightsPlugin>("Lights");

// Convert GitHubMCP Tools to Kernel Functions
Microsoft.SemanticKernel.KernelPlugin github_plugin = kernel.Plugins.AddFromFunctions("GitHub", github_tools.Select(aiFunction => aiFunction.AsKernelFunction()));

// Convert Azure MCP Tools to Kernel Functions
// I can't run the next line because the Azure functions included here have a dash in their name, which is not allowed in the KernelFunction name
// Microsoft.SemanticKernel.KernelPlugin azure_plugin = kernel.Plugins.AddFromFunctions("Azure", azure_tools.Select(aiFunction => aiFunction.AsKernelFunction()));


// Enable planning
// if "pure" OpenAI, please use      OpenAIPromptExecutionSettings
// in Azure OpenAI, we have     AzureOpenAIPromptExecutionSettings
var azureOpenAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
};
var kernelArguments = new KernelArguments(azureOpenAIPromptExecutionSettings) // optional            
{
    { "repository", "microsoft/semantic-kernel" }
};

string agent_name = "sk_chatcompletion_agent";
string instructions = "you are a clever agent";

Console.WriteLine("\nDefining a chat completion completion Agent...");

var sk_chatcompletion_agent = new ChatCompletionAgent
{
    Name = agent_name,
    Instructions = instructions,
    Kernel = kernel,
    Arguments = kernelArguments ?? new KernelArguments() // Provide a default value if kernelArguments is null
};

Console.WriteLine("...completion Agent is ready.");

// Create a history store the conversation, however use ChatHistoryAgentThread instead of ChatHistory, which is deprecated
var sk_chatcompletionagent_thread = new ChatHistoryAgentThread();

// Initiate a back-and-forth chat
string? user_input;
do
// #pragma warning disable CS8602 // Dereference of a possibly null reference.
{
    // Collect user input
    Console.Write(@"
Please ask me something, or type 'EXIT' to end the conversation.
Examples of questions you can ask:
- summarize the last four commits to the microsoft/semantic-kernel repository (e.g. GitHub MCP server usage),
- list all my Azure Storage Accounts (e.g. Azure MCP server usage),
- how many feets are there in a mile? (e.g. normal Chat Completion, to show how the history is stored),
- toggle the chandelier and tell me the status of all lights (e.g. Plugin usage),
- tell me a joke with no less than 200 words (e.g. normal Chat Completion, to show streaming features),

Your turn > ");
    user_input = Console.ReadLine();

    // Check if userInput is not null before adding it to the chat history
    if (!(string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase)))
    {

        var message = new ChatMessageContent(AuthorRole.User, user_input);

        //await foreach (ChatMessageContent response in sk_chatcompletion_agent.InvokeAsync(message: message, thread: sk_chatcompletionagent_thread))
        await foreach (StreamingChatMessageContent response in sk_chatcompletion_agent.InvokeStreamingAsync(message: message, thread: sk_chatcompletionagent_thread))
        {
            Console.Write($"{response.Content}");
        }

        /*
        var result = await kernel.InvokePromptAsync(user_input, new(azureOpenAIPromptExecutionSettings)).ConfigureAwait(false);

        Console.WriteLine($"\n\n{user_input}\n{result}");
        */

        Console.Write($"\n\nThere are {sk_chatcompletionagent_thread.ChatHistory.Count()} messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive. > ");
        var clear_history = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
        {
            await foreach (StreamingChatMessageContent response in sk_chatcompletion_agent.InvokeStreamingAsync(
                message: new ChatMessageContent(AuthorRole.User, "Reset lights status"),
                thread: sk_chatcompletionagent_thread))
            {
                // Console.Write($"{response.Content}");
            }

            sk_chatcompletionagent_thread.ChatHistory.Clear();
        }
    }
} while (!(string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase)));
// #pragma warning restore CS8602 // Dereference of a possibly null reference.

// delete the thread
if (sk_chatcompletionagent_thread.Id is not null)
{
    Console.WriteLine($"\nDeleting thread  id {sk_chatcompletionagent_thread.Id}...");
    await sk_chatcompletionagent_thread.DeleteAsync();
}

// deallocate the agent
Console.WriteLine($"Deallocating agent {sk_chatcompletion_agent.Id} that is just an instance of ChatCompletionAgent...");
sk_chatcompletion_agent = null;

Console.WriteLine("Application ends");
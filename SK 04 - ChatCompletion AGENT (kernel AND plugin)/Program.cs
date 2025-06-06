// Copyright (c) Microsoft. All rights reserved.

// **Documentation**: [`ChatHistoryAgentThread`](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-chat-agent?pivots=programming-language-csharp)

// Import packages

// dotnet add package Microsoft.Extensions.Logging --> <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.4" />
using Microsoft.Extensions.Logging;

// dotnet add package Microsoft.Extensions.DependencyInjection --> <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.5" />
using Microsoft.Extensions.DependencyInjection;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.55.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

// dotnet add package Microsoft.SemanticKernel.Agents.Core --> <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.55.0" />
using Microsoft.SemanticKernel.Agents; // needed for ChatCompletion

using DotNetEnv;
using AIPlugins;

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

// Enable planning
// if "pure" OpenAI, please use OpenAIPromptExecutionSettings
// in Azure OpenAI, we have     AzureChatPromptExecutionSettings
var azureOpenAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};
var kernelArguments = new KernelArguments(azureOpenAIPromptExecutionSettings)
// optional
{
    { "repository", "microsoft/semantic-kernel" }
};

string agent_name = "agent_name";
string instructions = "you are a clever agent";

Console.WriteLine("\nDefining completion Agent...");

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var sk_chatcompletion_agent = new ChatCompletionAgent
{
    Name = agent_name,
    Instructions = instructions,
    Kernel = kernel,
    Arguments = kernelArguments ?? new KernelArguments() // Provide a default value if kernelArguments is null
};
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

Console.WriteLine("...completion Agent is ready.");

// Create a history store the conversation, however use ChatHistoryAgentThread instead of ChatHistory, which is deprecated
var sk_chatcompletionagent_thread = new ChatHistoryAgentThread(); // new 

// Initiate a back-and-forth chat
string? user_input;
do
// #pragma warning disable CS8602 // Dereference of a possibly null reference.
{
    // Collect user input
    Console.Write("\n\nPls ask your question, e.g. 'Toggle chandelier light and tell me all lights status' > ");
    user_input = Console.ReadLine();

    // Check if userInput is not null before adding it to the chat history
    if (!(string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase)))
    {
        // plugin status must be kept by the history, not the kernel
        Microsoft.SemanticKernel.KernelPlugin lights_plugin = kernel.Plugins.AddFromType<LightsPlugin>("Lights");

        var message = new ChatMessageContent(AuthorRole.User, user_input);

        // await foreach (ChatMessageContent response in sk_chatcompletion_agent.InvokeAsync(message: message, thread: sk_chatcompletionagent_thread))
        await foreach (StreamingChatMessageContent response in sk_chatcompletion_agent.InvokeStreamingAsync(message: message, thread: sk_chatcompletionagent_thread))
        {
            Console.Write($"{response.Content}");
        }

        kernel.Plugins.Remove(lights_plugin);

        Console.Write($"\nThere are {sk_chatcompletionagent_thread.ChatHistory.Count()} messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive. > ");
        var clear_history = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
        {
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

// delete the agent
Console.WriteLine($"\nDeleting agent {sk_chatcompletion_agent.Id}...");
// await sk_chatcompletion_agent.DeleteAsync();

Console.WriteLine("Application ends");
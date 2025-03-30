// Copyright (c) Microsoft. All rights reserved.

// **Documentation**: [`ChatHistoryAgentThread`](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-chat-agent?pivots=programming-language-csharp)

// Import packages
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.44.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

// dotnet add package Microsoft.SemanticKernel.Agents.Core --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.44.0-preview" />
using Microsoft.SemanticKernel.Agents;

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
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

// Build the kernel
Kernel kernel = builder.Build();

// Add a plugin (the LightsPlugin class is defined in its dedicated file LightsPlugin.cs)
kernel.Plugins.AddFromType<LightsPlugin>("Lights");

// Enable planning
// if "pure" OpenAI, please use OpenAIPromptExecutionSettings
// in Python we have AzureChatPromptExecutionSettings
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
var agent = new ChatCompletionAgent
{
    Name = agent_name,
    Instructions = instructions,
    Kernel = kernel,
    Arguments = kernelArguments ?? new KernelArguments() // Provide a default value if kernelArguments is null
};
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

Console.WriteLine("...completion Agent is ready.");

// Create a history store the conversation, however use ChatHistoryAgentThread instead of ChatHistory, which is deprecated
var agentThread = new ChatHistoryAgentThread(); // new 

// Initiate a back-and-forth chat
string? userInput;
do
#pragma warning disable CS8602 // Dereference of a possibly null reference.
{
    // Collect user input
    Console.Write("User > ");
    userInput = Console.ReadLine();

    // Check if userInput is not null before adding it to the chat history
    if (userInput != null)
    {
        var message = new ChatMessageContent(AuthorRole.User, userInput);

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        await foreach (ChatMessageContent response in agent.InvokeAsync(message: message, thread: agentThread))
        {
            Console.WriteLine($"{response.Content}");
        }
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    }
} while (!string.IsNullOrWhiteSpace(userInput) && !userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase));
#pragma warning restore CS8602 // Dereference of a possibly null reference.

Console.WriteLine("Application ends");
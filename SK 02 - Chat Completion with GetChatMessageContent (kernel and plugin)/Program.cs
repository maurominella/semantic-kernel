// Copyright (c) Microsoft. All rights reserved.

// See https://aka.ms/new-console-template for more information

// to create the C# console app: "dotnet new console --framework net8.0" followed by "dotnet restore" (not needed after SDK 2.0, just use dotnet build or dotnet run)

// this sample implements the OpenAI ChatCompletion object (instance of IChatCompletionService) 
// that we use to call GetChatMessageContentAsync

// Base sample: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-chat-agent?pivots=programming-language-csharp

// dotnet add package Microsoft.Extensions.Logging --> <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.4" />
using Microsoft.Extensions.Logging;

// dotnet add package Microsoft.Extensions.DependencyInjection --> <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.4" />
using Microsoft.Extensions.DependencyInjection;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.46.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

// dotnet add package DotNetEnv --> <PackageReference Include="DotNetEnv" Version="3.1.1" />
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

// Build the kernel from the builder that already contains ChatCompletionService + Logging services
Kernel kernel = builder.Build();

// Add a plugin (the LightsPlugin class is defined in its dedicated file LightsPlugin.cs)
// kernel.Plugins.AddFromType<LightsPlugin>("Lights");

// Enable planning
var openAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

// Create a history store the conversation
var history = new ChatHistory();

// Extract ChatCompletionService from the kernel
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Initiate a back-and-forth chat
string? user_input;
do
#pragma warning disable CS8602 // Dereference of a possibly null reference.
{
    // Collect user input
    Console.Write("\n\nPls ask your question, e.g. 'Toggle chandelier light and tell me all lights status' > ");
    user_input = Console.ReadLine();

    // Check if userInput is not null before adding it to the chat history
    if (!string.IsNullOrWhiteSpace(user_input))
    {
        // plugin status must be kept by the history, not the kernel
        Microsoft.SemanticKernel.KernelPlugin lights_plugin = kernel.Plugins.AddFromType<LightsPlugin>("Lights");
        history.AddUserMessage(user_input);

        // Get the response from the AI
        var result = await chatCompletionService.GetChatMessageContentAsync(
            history,
            executionSettings: openAIPromptExecutionSettings,
            kernel: kernel);

        // Print the results
        Console.WriteLine("Assistant > " + result);

        // Add the message from the agent to the chat history
        history.AddMessage(result.Role, result.Content ?? string.Empty);
        kernel.Plugins.Remove(lights_plugin);

        Console.Write($"\nThere are {history.ToList().Count} messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive. > ");
        var clear_history = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
        {
            history.Clear();
        }
    }


} while (!(string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase)));
#pragma warning restore CS8602 // Dereference of a possibly null reference.

Console.WriteLine("Application ends");
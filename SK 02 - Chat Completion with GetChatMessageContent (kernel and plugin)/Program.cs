// Copyright (c) Microsoft. All rights reserved.

// See https://aka.ms/new-console-template for more information

// to create the C# console app: "dotnet new console --framework net8.0" followed by "dotnet restore" (not needed after SDK 2.0, just use dotnet build or dotnet run)

// this sample implements the OpenAI ChatCompletion object (instance of IChatCompletionService) 
// that we use to call GetChatMessageContentAsync

// Base sample: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-chat-agent?pivots=programming-language-csharp

#region Libraries and Namespaces
// dotnet add package Microsoft.Extensions.Logging.Console --> <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.5" />
// dotnet add package Microsoft.Extensions.Logging --> <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.5" />
using Microsoft.Extensions.Logging; // needed for LogLevel

// dotnet add package Microsoft.Extensions.DependencyInjection --> <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.5" />
using Microsoft.Extensions.DependencyInjection; // needed for AddLogging

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.54.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

// dotnet add package DotNetEnv --> <PackageReference Include="DotNetEnv" Version="3.1.1" />
using DotNetEnv;

using AIPlugins; // contains the LightsPlugin class
#endregion

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


Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {Env.GetString("AZURE_OPENAI_ENDPOINT")}\nAZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME")}");

// Create the kernel builder with the pointer to Azure OpenAI
Microsoft.SemanticKernel.IKernelBuilder builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
    endpoint: Env.GetString("AZURE_OPENAI_ENDPOINT"),
    apiKey: Env.GetString("AZURE_OPENAI_API_KEY"),
    deploymentName: Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"));

// Use the kernel builder to add enterprise components (for logging, in this case)
builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.None));

// Build the kernel from the builder that already contains ChatCompletionService + Logging services
Kernel kernel = builder.Build();

// Add a plugin (the LightsPlugin class is defined in its dedicated file LightsPlugin.cs)
// kernel.Plugins.AddFromType<LightsPlugin>("Lights");

// Enable planning
// if "pure" OpenAI, please use      OpenAIPromptExecutionSettings
// in Azure OpenAI, we have     AzureOpenAIPromptExecutionSettings
var azureOpenAIPromptExecutionSettings = new AzureOpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};
var kernelArguments = new KernelArguments(azureOpenAIPromptExecutionSettings) // optional            
{
    { "repository", "microsoft/semantic-kernel" }
};

// Create a history store the conversation
var history = new ChatHistory();

// Extract ChatCompletionService from the kernel
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Initiate a back-and-forth chat
string? user_input;
bool time_to_exit = false;
do
#pragma warning disable CS8602 // Dereference of a possibly null reference.
{
    // Collect user input
    Console.Write(@"
Please ask me something, or type 'EXIT' to end the conversation.
Examples of questions you can ask:
- how many feets are there in a mile? (e.g. normal Chat Completion, to show how the history is stored),
- toggle the chandelier and tell me the status of all lights (e.g. Plugin usage),
- tell me a joke with no less than 200 words (e.g. normal Chat Completion, to show streaming features),

Your turn > ");
    user_input = Console.ReadLine();
    time_to_exit = (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase));

    // Check if userInput is not null before adding it to the chat history
    if (!string.IsNullOrWhiteSpace(user_input))
    {
        // plugin status must be kept by the history, not the kernel
        Microsoft.SemanticKernel.KernelPlugin lights_plugin = kernel.Plugins.AddFromType<LightsPlugin>("Lights");
        history.AddUserMessage(user_input);

        // Get the response from the AI
        var result = await chatCompletionService.GetChatMessageContentAsync(
            history,
            executionSettings: azureOpenAIPromptExecutionSettings,
            kernel: kernel);

        // Print the results
        Console.WriteLine("Assistant > " + result);

        // Add the message from the agent to the chat history
        history.AddMessage(result.Role, result.Content ?? string.Empty);
        kernel.Plugins.Remove(lights_plugin);

        if (!time_to_exit)
        {

            Console.Write($"\nThere are {history.ToList().Count} messages in the history. Enter 'Y' if you want to clear the status, or anything else to keep thread and plugins alive. > ");
            var clear_history = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
            {
                history.Clear();
            }
        }
    }

} while (!time_to_exit);
#pragma warning restore CS8602 // Dereference of a possibly null reference.

Console.WriteLine("Application ends");
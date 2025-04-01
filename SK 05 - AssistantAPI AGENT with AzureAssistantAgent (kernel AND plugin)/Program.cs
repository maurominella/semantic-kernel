// Copyright (c) Microsoft. All rights reserved.

// Last update: March 31st, 2025

// See https://aka.ms/new-console-template for more information

// OpenAIAssistantAgent is the KEY of this exercise 
// Migration guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/agent-framework-rc-migration-guide?pivots=programming-language-csharp
// Docs: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/assistant-agent?pivots=programming-language-csharp
// Class: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.agents.openai.openaiassistantagent?view=semantic-kernel-dotnet

using DotNetEnv;
using MyApp.Plugins;

// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.13.2" />
using Azure.Identity;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.44.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" Version="1.44.0-preview" />
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.AI.OpenAI;
using OpenAI.Assistants;
using OpenAI.Files;



#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

internal class Program
{
    private static async Task Main(string[] args)
    {
        string projectRoot;

        Console.WriteLine("Application starts");

        // Get the base directory
        DirectoryInfo baseDirectory = new(AppDomain.CurrentDomain.BaseDirectory);

        // retrieve the project root folder
        if (baseDirectory.Parent.Parent.Name == "bin")
        {
            projectRoot = baseDirectory.Parent.Parent.Parent.FullName;
        }
        else
        {
            projectRoot = baseDirectory.FullName;
        }

        string envFilePath = Path.Combine(projectRoot, "./../../config/credentials_my.env");
        Console.WriteLine($"envFilePath: {envFilePath}");

        // Load the environment variables from the .env file
        Env.Load(envFilePath);

        // Instantiation of the Client for Azure OpenAI 
        AzureOpenAIClient openaiClient = OpenAIAssistantAgent.CreateAzureOpenAIClient(
            new AzureCliCredential(), new Uri(Env.GetString("AZURE_OPENAI_ENDPOINT")));

        // just for testing: upload a file
        OpenAIFileClient fileClient = openaiClient.GetOpenAIFileClient();
        OpenAIFile fileInfo = await fileClient.UploadFileAsync("product_info_1.md", FileUploadPurpose.Assistants);

#pragma warning disable OPENAI001 // 'OpenAI.Assistants.AssistantClient' is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        // Using the Azure OpenAI Client, now extract another Client for OpenAI Assistant Agent
        AssistantClient assistantClient = openaiClient.GetAssistantClient();
#pragma warning restore OPENAI001

        // using the Client for OpenAI Assistant Agent, now either...
        // ...EXTRACT the definition for a specific EXISTING OpenAI Assistant:
        // var assistantDefinition = await assistantClient.GetAssistantAsync(assistantId: "");

        //... or CREATE the definition for a NEW OpenAI Assistant:
        string agent_name = "mauromi_assistant_agent_c#";
        string instructions = "you are a clever agent";

        var assistantDefinition = await assistantClient.CreateAssistantAsync(
            modelId: Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            name: agent_name,
            instructions: instructions,
            enableCodeInterpreter: true
        );

        KernelPlugin lightsPlugin = KernelPluginFactory.CreateFromType<LightsPlugin>();

        // using the definition of a specific (new or existing) OpenAI Assistant, now we may directly instantiate an OpenAIAssistantAgent 
        var agent = new OpenAIAssistantAgent(definition: assistantDefinition, client: assistantClient, plugins: [lightsPlugin]);


        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {Env.GetString("AZURE_OPENAI_ENDPOINT")}\n" +
            $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME")}");

        // Add enterprise logging components
        // TBI


        // Initiate a back-and-forth chat
        bool isComplete = false;
        try
        {
            string? userInput;
            do
            {
                Console.WriteLine();
                Console.Write("User > ");
                // Collect user input
                userInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(userInput))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    isComplete = true;
                    break;
                }

                var message = new ChatMessageContent(AuthorRole.User, userInput);

                try
                {
                    await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(message: message))
                    {
                        // Display response.
                        Console.Write($"{response.Content}");

                        // SK does NOT need to add the response to the thread history
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    isComplete = true;
                    break;
                }
                Console.WriteLine();

            } while (!isComplete);
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Cleaning-up...");
            var file_list = await fileClient.GetFilesAsync();
            foreach (var f in file_list.Value)
            {
                Console.WriteLine($"Deleting file {f.Filename} ({f.Id})...");
                await fileClient.DeleteFileAsync(f.Id);
            }

            Console.WriteLine($"Deleting assistant {agent.Name} ({agent.Id})...");
            await Task.WhenAll(
                [
                    assistantClient.DeleteAssistantAsync(agent.Id),
                ]);
        }

        if (isComplete)
        {
            return; // Terminate the program after the finally block
        }
    }
}

#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
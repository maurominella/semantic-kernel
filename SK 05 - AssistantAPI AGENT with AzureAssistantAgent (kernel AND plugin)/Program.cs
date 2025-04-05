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

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.45.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.45.0-preview" />
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.AI.OpenAI;
using OpenAI.Assistants;
using OpenAI.Files;
using OpenAI.VectorStores;

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

        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {Env.GetString("AZURE_OPENAI_ENDPOINT")}\n" +
            $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME")}");

        // Instantiation of the Client for Azure OpenAI 
        AzureOpenAIClient openaiClient = OpenAIAssistantAgent.CreateAzureOpenAIClient(
            new AzureCliCredential(), new Uri(Env.GetString("AZURE_OPENAI_ENDPOINT")));

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        VectorStoreClient storeClient = openaiClient.GetVectorStoreClient();
        CreateVectorStoreOperation operation = await storeClient.CreateVectorStoreAsync(waitUntilCompleted: true);
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        string storeId = operation.VectorStoreId;

        // OpenAIAssistantFileSearch https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-assistant-search?pivots=programming-language-csharp
        OpenAIFileClient fileClient = openaiClient.GetOpenAIFileClient();

        string[] s_files_to_search_in =
        [
            "trailmaster_product_info_1.md",
        ];


        Dictionary<string, OpenAIFile> fileReferences = [];
        foreach (string f in s_files_to_search_in)
        {
            OpenAIFile fileInfo = await fileClient.UploadFileAsync(f, FileUploadPurpose.Assistants);
            await storeClient.AddFileToVectorStoreAsync(storeId, fileInfo.Id, waitUntilCompleted: true);
            fileReferences.Add(fileInfo.Id, fileInfo);
        }


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
            enableCodeInterpreter: true,
            enableFileSearch: true,
            vectorStoreId: storeId
        );

        KernelPlugin lightsPlugin = KernelPluginFactory.CreateFromType<LightsPlugin>();

        // using the definition of a specific (new or existing) OpenAI Assistant, now we may directly instantiate an OpenAIAssistantAgent 
        var agent = new OpenAIAssistantAgent(definition: assistantDefinition, client: assistantClient, plugins: [lightsPlugin]);

        // Add enterprise logging components
        // TBI

        // Create the conversation thread
        Console.WriteLine("Creating thread...");
        OpenAIAssistantAgentThread agentThread = new(client: assistantClient);

        // Initiate a back-and-forth chat
        bool isComplete = false;
        try
        {
            string? userInput;
            do
            {
                Console.WriteLine();
                Console.Write("User (e.g. 'What is the sub category of the product TrailMaster X4 Tent?' or 'How many interior pockets does Trailmaster have?') > ");
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
                    await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(message: message, thread: agentThread))
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
            Console.WriteLine("\nCleaning-up...");

            // collect the thread messages: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-streaming?pivots=programming-language-csharp
            ChatMessageContent[] messages = await agentThread.GetMessagesAsync().ToArrayAsync();
            int i = 0;
            foreach (var message in messages.Reverse())
            {
                Console.WriteLine($"Message {++i}: {message.Content}");
            }

            Console.WriteLine($"Deleting thread {agentThread.Id}...");
            await agentThread.DeleteAsync();

            Console.WriteLine($"Deleting store {storeId}...");
            await storeClient.DeleteVectorStoreAsync(storeId);

            Console.WriteLine($"Deleting assistant {agent.Name} ({agent.Id})...");
            await assistantClient.DeleteAssistantAsync(agent.Id);

            foreach (var f in fileReferences.Values)
            {
                Console.WriteLine($"Deleting file {f.Filename} ({f.Id})...");
                await fileClient.DeleteFileAsync(f.Id);
            }

            /* alternatively, asynchronously wait for all the tasks to complete before moving on to the next line of code
            await Task.WhenAll( // The program will 
                [
                    agentThread.DeleteAsync(),
                    assistantClient.DeleteAssistantAsync(agent.Id),
                    storeClient.DeleteVectorStoreAsync(storeId),
                ]);
            */
        }

        if (isComplete)
        {
            return; // Terminate the program after the finally block
        }
    }
}

#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
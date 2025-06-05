// Copyright (c) Microsoft. All rights reserved.

// Last update: March 31st, 2025

// See https://aka.ms/new-console-template for more information

// OpenAIAssistantAgent is the KEY of this exercise 
// Migration guide: https://learn.microsoft.com/en-us/semantic-kernel/support/migration/agent-framework-rc-migration-guide?pivots=programming-language-csharp
// Docs: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/assistant-agent?pivots=programming-language-csharp
// Class: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.agents.openai.openaiassistantagent?view=semantic-kernel-dotnet

using DotNetEnv;
using MyApp.Plugins;

// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.0" />
using Azure.Identity;

// dotnet add package Microsoft.SemanticKernel --> <PackageReference Include="Microsoft.SemanticKernel" Version="1.55.0" />
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --><PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.55.0-preview" />
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
        AssistantClient assistant_client = openaiClient.GetAssistantClient();
#pragma warning restore OPENAI001

        // using the Client for OpenAI Assistant Agent, now either...
        // ...EXTRACT the definition for a specific EXISTING OpenAI Assistant:
        // var assistantDefinition = await assistantClient.GetAssistantAsync(assistantId: "");

        //... or CREATE the definition for a NEW OpenAI Assistant:
        string agent_name = "mauromi_assistant_agent_c#";
        string instructions = "you are a clever agent";

        var assistant_definition = await assistant_client.CreateAssistantAsync(
            modelId: Env.GetString("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            name: agent_name,
            instructions: instructions,
            enableCodeInterpreter: true,
            enableFileSearch: true,
            vectorStoreId: storeId
        );

        // using the definition of a specific (new or existing) OpenAI Assistant, now we may directly instantiate an OpenAIAssistantAgent 
        var sk_assistantagent = new OpenAIAssistantAgent(definition: assistant_definition, client: assistant_client); //, plugins: [lightsPlugin]);

        // Add enterprise logging components
        // TBI

        // Create the conversation thread
        Console.WriteLine("Creating thread...");
        OpenAIAssistantAgentThread? sk_assistant_thread = null;

        // Initiate a back-and-forth chat
        string? user_input;
        do
        {
            // Collect user input
            Console.Write("\n\nPls ask your question, e.g. 'How many interior pockets does Trailmaster have?' OR 'Toggle the porch light and tell me all statuses' > ");
            user_input = Console.ReadLine();

            if (!(string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase)))
            {
                if (sk_assistant_thread == null)
                {
                    sk_assistant_thread = new(client: assistant_client);
                }
                // plugin status must be kept by the history, not the kernel             
                Microsoft.SemanticKernel.KernelPlugin lights_plugin = sk_assistantagent.Kernel.Plugins.AddFromType<LightsPlugin>("Lights");

                var message = new ChatMessageContent(AuthorRole.User, user_input);

                await foreach (StreamingChatMessageContent response in sk_assistantagent.InvokeStreamingAsync(message: message, thread: sk_assistant_thread))
                {
                    // Display response.
                    Console.Write($"{response.Content}");

                    // SK does NOT need to add the response to the thread history
                }

                sk_assistantagent.Kernel.Plugins.Clear(); // better than sk_assistantagent.Kernel.Plugins.Remove(lights_plugin);

                // collect the thread messages: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-streaming?pivots=programming-language-csharp
                ChatMessageContent[] messages = await sk_assistant_thread.GetMessagesAsync().ToArrayAsync();

                Console.Write($"\nThere are {messages.Length} messages in the history. Enter 'Y' inf you want to clear the status, or anything else to keep the thread and its messages alive > ");
                var clear_history = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
                {
                    Console.WriteLine($"Deleting thread {sk_assistant_thread.Id}...");
                    await sk_assistant_thread.DeleteAsync();
                    sk_assistant_thread = null;
                }
            }

            Console.WriteLine();

        } while (!(string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase)));
        Console.WriteLine("\nCleaning-up...");

        if (sk_assistant_thread != null && sk_assistant_thread.Id != null)
        {
            Console.WriteLine($"Deleting thread {sk_assistant_thread.Id}...");
            await sk_assistant_thread.DeleteAsync();
        }

        Console.WriteLine($"Deleting store {storeId}...");
        await storeClient.DeleteVectorStoreAsync(storeId);

        Console.WriteLine($"Deleting assistant {sk_assistantagent.Name} ({sk_assistantagent.Id})...");
        await assistant_client.DeleteAssistantAsync(sk_assistantagent.Id);

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
}

#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
// Copyright (c) Microsoft. All rights reserved.

// Last update: March 31st, 2025

// See https://aka.ms/new-console-template for more information

// OpenAIAssistantAgent is the KEY of this exercise 
// Docs: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/assistant-agent?pivots=programming-language-csharp
// OpenAIAssistantAgent Class: https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.agents.openai.openaiassistantagent?view=semantic-kernel-dotnet
// OpenAIAssistantFileSearch https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/examples/example-assistant-search?pivots=programming-language-csharp
// GA Migration guide (AI Foundry): https://learn.microsoft.com/en-us/semantic-kernel/support/migration/azureagent-foundry-ga-migration-guide?pivots=programming-language-csharp

/*
Package: Microsoft.SemanticKernel.Agents.OpenAI (prerelease) - Based on OpenAI Assistant API
- Version: 1.55.0-preview (as of 2025-06-07)
- Source: https://www.nuget.org/packages/Microsoft.SemanticKernel.Agents.OpenAI/1.55.0-preview
- Underlying SDK: 
  - OpenAI or Azure.AI.OpenAI
  - Not GA
- Available on both OpenAI and Azure endpoints
- Open AI announced deprecation by early 2026 (going away)
*/
#region Libraries and Namespaces
// dotnet add package Microsoft.SemanticKernel.Agents.OpenAI --prerelease --> <PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" Version="1.61.0-preview" />
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.AI.OpenAI;
using OpenAI.VectorStores;
using OpenAI.Files;
using OpenAI.Assistants;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel; // does not require Microsoft.SemanticKernel module
using Microsoft.SemanticKernel.ChatCompletion;

// dotnet add package Azure.Identity --> <PackageReference Include="Azure.Identity" Version="1.14.2" />
using Azure.Identity;

// contains the LightsPlugin class
using MyApp.Plugins;

// contains the AISettings class
namespace LLMSettings;
#endregion

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("\n+++++++++++++++++ Application starts +++++++++++++++++");

        #region Environment Configuration
        Console.WriteLine("\n\n\n+++++++++++++++++ Environment Configuration +++++++++++++++++\n");
        // Load configuration from environment variables or user secrets.
        var ai_settings = new AISettings();
        Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {ai_settings.AzureOpenAI.Endpoint}\n" +
        $"AZURE_OPENAI_CHAT_DEPLOYMENT_NAME: {ai_settings.AzureOpenAI.ChatModelDeployment}\n" +
        $"PROJECT_ENDPOINT: {ai_settings.AzureOpenAI.ProjectEndpoint}\n");
        #endregion

        // Instantiation of the Assistant API Client for Azure OpenAI 
        AzureOpenAIClient azure_openai_client = OpenAIAssistantAgent.CreateAzureOpenAIClient(
            credential: new AzureCliCredential(), endpoint: new Uri(ai_settings.AzureOpenAI.Endpoint));

        // Get the File Client
        OpenAIFileClient file_client = azure_openai_client.GetOpenAIFileClient();

        // Enumerate the existing files
        IEnumerable<OpenAIFile> uploaded_files = (await file_client.GetFilesAsync()).Value;
        Console.WriteLine($"There are {uploaded_files.Count()} pre-existing files(s)");

        // delete all files
        foreach (var file in uploaded_files)
        {
            Console.WriteLine($"Deleting File <Id: {file.Id}, Name: {file.Filename}>...");
            await file_client.DeleteFileAsync(file.Id);
        }

        // Identify and Upload the files to search in, with the openai assistant search feature
        var s_openaifiles_to_search_in = new List<OpenAIFile>();
        string[] s_files_to_search_in =
        [
            "data/search_files/trailmaster_product_info_1.md",
        ];
        foreach (string f in s_files_to_search_in)
        {
            OpenAIFile fileInfo = await file_client.UploadFileAsync(f, FileUploadPurpose.Assistants);
            s_openaifiles_to_search_in.Add(fileInfo);
            Console.WriteLine($"File <{f}> uploaded as <{fileInfo.Id}>");
        }

        // Identify and Upload the files to work with with openai assistant code interpreter
        var s_id_files_to_work_with = new List<string>();
        string[] s_files_to_work_with =
        [
            "data/codeinterpreter_files/turbines.xlsx",
        ];
        foreach (string f in s_files_to_work_with)
        {
            OpenAIFile fileInfo = await file_client.UploadFileAsync(f, FileUploadPurpose.Assistants);
            s_id_files_to_work_with.Add(fileInfo.Id);
            Console.WriteLine($"File <{f}> uploaded as <{fileInfo.Id}>");
        }

        // Get the VectorStore Client
        VectorStoreClient storeClient = azure_openai_client.GetVectorStoreClient();

        // Enumerate the existing vectors stores
        var vectorStores = await storeClient.GetVectorStoresAsync().ToListAsync();

        // Delete all vector stores
        Console.WriteLine($"There are {vectorStores.Count} pre-existing Vector Store(s)");
        foreach (VectorStore v in vectorStores)
        {
            Console.WriteLine($"Deleting Vector Store with Id = <{v.Id}>...");
            await storeClient.DeleteVectorStoreAsync(vectorStoreId: v.Id);
        }

        // Create a Vector Store and add the files to it (only the ones to search in)
        string storeId = (await storeClient.CreateVectorStoreAsync(waitUntilCompleted: true)).VectorStoreId;
        Console.WriteLine($"New Vector Store created with Id = <{storeId}>");

        foreach (OpenAIFile file in s_openaifiles_to_search_in) // if you want **ALL** files: (await fileClient.GetFilesAsync()).Value)
        {
            Console.WriteLine($"Adding new file <Id: {file.Id}, Name: {file.Filename}> to the <{storeId}> vector store...");
            await storeClient.AddFileToVectorStoreAsync(storeId, file.Id, waitUntilCompleted: true);
        }

        // Get the ASSISTANT API CLIENT
        AssistantClient assistant_client = azure_openai_client.GetAssistantClient();

        // Enumerate all assistants
        var assistants = await assistant_client.GetAssistantsAsync().ToListAsync();

        // Delete all assistants
        Console.WriteLine($"There are {assistants.Count} pre-existing assistant(s)");
        foreach (Assistant a in assistants)
        {
            Console.WriteLine($"Deleting Assistant <Id: {a.Id}, Name: {a.Name}>...");
            await assistant_client.DeleteAssistantAsync(a.Id);
        }

        // Create an ASSISTANT (or ASSISTANT DEFINITION)
        Assistant assistant = await assistant_client.CreateAssistantAsync(
            modelId: ai_settings.AzureOpenAI.ChatModelDeployment, // deployment name
            name: "mauromi's assistant",
            description: "My first assistant",
            instructions: "You are a clever assistant",
            enableCodeInterpreter: true,
            codeInterpreterFileIds: s_id_files_to_work_with,
            enableFileSearch: true,
            vectorStoreId: storeId); // includes s_opeaifiles_to_search_in
        Console.WriteLine($"Created new Assistant <{assistant.Name}> with Id <{assistant.Id}>");

        // Create an assistant AGENT
        var sk_assistantapi_agent = new OpenAIAssistantAgent(definition: assistant, client: assistant_client);

        // Add a plugin to the assistant agent
        sk_assistantapi_agent.Kernel.Plugins.AddFromType<LightsPlugin>("Lights");

        // Here we just define (without creating) the AgentThread variable
        // An AgentThread will be started and returned as part of the response
        AgentThread? agent_thread = null;

        // Initiate a back-and-forth chat
        bool exit_chat = false;
        string? user_input;

        do
        {
            Console.Write($@"
Please ask me something, or type 'EXIT' to end the conversation.
Examples of questions you can ask:
- tell me a joke with no less than 200 words (e.g. normal Chat Completion),
- toggle the chandelier and tell me the status of all lights (e.g. Plugin usage),
- what's the Season Rating for the TrailMaster X4 Tent? (e.g. Assistant API's File Search),
- use turbines.xlsx to find the earliest Maintenance_Date among the turbine with highest voltage (e.g. Assistant API's Code Interpreter),

Your turn > ");

            // Collect user input
            user_input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(user_input) || user_input.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                exit_chat = true;
                break;
            }

            // Generate the agent response(s)
            await foreach (AgentResponseItem<StreamingChatMessageContent> response in sk_assistantapi_agent.InvokeStreamingAsync( // streaming version
            // await foreach (AgentResponseItem<ChatMessageContent> response in assistant_agent.InvokeAsync( // non streaming version
                new ChatMessageContent(AuthorRole.User, user_input), thread: agent_thread))
            {
                // Process agent response(s)...
                Console.Write($"{response.Message.Content}");
                agent_thread = response.Thread;
            }

            Console.Write($"\n\nEnter 'Y' if you want to clear the history, or anything else to keep the thread and its messages alive > ");
            var clear_history = Console.ReadLine();
            // Delete the thread if no longer needed
            if (!string.IsNullOrWhiteSpace(clear_history) && clear_history.ToUpper().Trim()[0] == 'Y')
            {
                // await agent_thread.DeleteAsync();
                agent_thread = null;
            }

        } while (!exit_chat);

    }
}
### constants and libraries
import asyncio
import os
from dotenv import load_dotenv # requires python-dotenv
from semantic_kernel import Kernel


async def main():

    load_dotenv("./../config/credentials_my.env")
    print(f"os.environ['AZURE_OPENAI_ENDPOINT']: {os.environ['AZURE_OPENAI_ENDPOINT']}")

    ### initialize the kernel
    kernel = Kernel()

    ### add enterprise services (loggin)
    import logging

    # Set the logging level for semantic_kernel.kernel to DEBUG.
    logging.basicConfig(
        format="[%(asctime)s - %(name)s:%(lineno)d - %(levelname)s] %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )
    logging.getLogger("kernel").setLevel(logging.DEBUG)

    ### Create an Azure OpenAI chat completion, and add it to the Kernel
    # Remove all services so that this cell can be re-run without restarting the kernel
    kernel.remove_all_services()

    from semantic_kernel.connectors.ai.open_ai import AzureChatCompletion
    chat_completion=AzureChatCompletion(service_id="default")
    kernel.add_service(chat_completion)

    ### Create a native plugin through a custom function
    from typing import Annotated
    from semantic_kernel.functions import kernel_function

    class LightsPlugin:
        lights = [
            {"id": 0, "name": "Table Lamp", "is_on": False},
            {"id": 1, "name": "Porch light", "is_on": False},
            {"id": 2, "name": "Chandelier", "is_on": True},
        ]

        @kernel_function(
            name="get_lights", # <<<=== DIFFERENT FROM THE FUNCTION NAME <get_state>, which will be ignored
            description="Gets a list of lights and their current state",
        )
        def get_state(
            self,
        ) -> Annotated[str, "the output is a string"]:
            """Gets a list of lights and their current state."""
            return self.lights

        @kernel_function(
            name="change_state",
            description="Changes the state of the light",
        )
        def change_state(
            self,
            id: int,
            is_on: bool,
        ) -> Annotated[str, "the output is a string"]:
            """Changes the state of the light."""
            for light in self.lights:
                if light["id"] == id:
                    light["is_on"] = is_on
                    return light
            return None
        

    ### Testing the plugin (optional)
    # Cell to execute each time you want to reset and toggle light 1
    lp = LightsPlugin()  # (Re-)initialize lp to default state
    print("Initial state of the lights", lp.get_state(), "\n")
    lp.change_state(id=1, is_on=not(lp.lights[0]["is_on"]))  # Toggle light 1
    print("New state of the lights", lp.get_state(), "\n")  # Print current state of all lights


    ### Add the native plugin to the Kernel
    kernel.add_plugin(
        LightsPlugin(),
        plugin_name="Lights",
    )


    ### Enable Planning
    from semantic_kernel.connectors.ai.function_choice_behavior import FunctionChoiceBehavior
    from semantic_kernel.connectors.ai.open_ai.prompt_execution_settings.azure_chat_prompt_execution_settings import (
        AzureChatPromptExecutionSettings,
    )

    execution_settings = AzureChatPromptExecutionSettings()
    execution_settings.function_choice_behavior= FunctionChoiceBehavior.Auto() # Auto() or NoneInvoke()


    ### Chat history
    from semantic_kernel.contents.chat_history import ChatHistory
    # Create a history of the conversation
    history = ChatHistory()
    # Add user input to the history
    history.add_user_message("Please toggle all the lights")

    history

    ### Get the response from the AI
    result = await chat_completion.get_chat_message_contents(
        chat_history=history,
        settings=execution_settings,
        kernel=kernel)

    #  Print the results
    print("Assistant > " + str(result))
    print("Final state of the lights", lp.get_state(), "\n")


# Run the main function
if __name__ == "__main__":
    asyncio.run(main())
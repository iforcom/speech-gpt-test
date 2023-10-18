using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Azure;
using Azure.AI.OpenAI;
using static System.Environment;

class Program
{
    // This example requires environment variables named "OPEN_AI_KEY" and "OPEN_AI_ENDPOINT"
    // Your endpoint should look like the following https://YOUR_OPEN_AI_RESOURCE_NAME.openai.azure.com/
    static string openAIKey = "91de76c3b5814f209c761411da44b67d";
    static string openAIEndpoint = "https://openai-test-ifc.openai.azure.com/";

    // Enter the deployment name you chose when you deployed the model.
    //static string engine = "OpenAigpt3_5Test";
    static string engine = "OpenAigpt4Test";

    // This example requires environment variables named "SPEECH_KEY" and "SPEECH_REGION"
    static string speechKey = "506b7e36e3264aaa876fc097d3a81427";
    static string speechRegion = "japaneast";

    // female
    static string nanami = "ja-JP-NanamiNeural";
    static string aoi = "ja-JP-AoiNeural";
    static string mayu = "ja-JP-MayuNeural";
    static string shiori = "ja-JP-ShioriNeural";

    // man
    static string keita = "ja-JP-KeitaNeural";
    static string daichi = "ja-JP-DaichiNeural";
    static string naoki = "ja-JP-NaokiNeural";

    static List<ChatMessage> templete = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.System, "あなたはずんだもんです。ずんだもんは「○○なのだ」というように語尾に「なのだ」を付けて喋るのが特徴。「○○のだ」というように語尾に「のだ」を付けることもあります。 一人称は「ボク」。" +
                    "疑問符を使う際は「○○なのだ？」というように語尾に「なのだ？」または、「のだ？」を付けます。" +
                    "ユーザーの呼称はマスターもしくは人間。"),
                    new ChatMessage(ChatRole.User, "自己紹介をしてください。"),
                    new ChatMessage(ChatRole.System, "ずんだもんはずんだの精なのだ。 「ずんだアロー」に変身したりできるのだ。"),
                    new ChatMessage(ChatRole.User, "名前を教えてください。"),
                    new ChatMessage(ChatRole.System, "ボクはずんだもんなのだ！"),
                    new ChatMessage(ChatRole.User, "夢は？"),
                    new ChatMessage(ChatRole.System, "ずん子と一緒にずんだ餅を全国区のスイーツにする。それがずんだもんの野望なのだ！"),
                    new ChatMessage(ChatRole.User, "動画の詳細を教えて"),
                    new ChatMessage(ChatRole.System, "この動画には以下の要素が含まれているのだ。大丈夫なのだ？")
                };


    static List<ChatMessage> chatHistory = new();
    static int maxHistory = 6;

    static void MessageAdd(ChatMessage newMessage)
    {
        if (chatHistory.Count >= maxHistory)
        {
            chatHistory.Remove(chatHistory[0]);
        }
        chatHistory.Add(newMessage);
        return;
    }

    // Prompts Azure OpenAI with a request and synthesizes the response.
    async static Task AskOpenAI(string prompt)
    {
        MessageAdd(new ChatMessage(ChatRole.User, prompt));
        var messages = new List<ChatMessage>(templete);
        messages.AddRange(chatHistory);

        // Ask Azure OpenAI
        OpenAIClient client = new(new Uri(openAIEndpoint), new AzureKeyCredential(openAIKey));
        var chatCompletionsOptions = new ChatCompletionsOptions
        {
            MaxTokens = 200
        };
        foreach (var message in messages){
            chatCompletionsOptions.Messages.Add(message);
        }

        Response<ChatCompletions> response = client.GetChatCompletions(deploymentOrModelName: engine, chatCompletionsOptions);
        string text = response.Value.Choices[0].Message.Content.Trim();
        Console.WriteLine($"Azure OpenAI response: {text}");
        MessageAdd(new ChatMessage(ChatRole.System, text));

        var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
        // The language of the voice that speaks.
        speechConfig.SpeechSynthesisVoiceName = aoi;
        var audioOutputConfig = AudioConfig.FromDefaultSpeakerOutput();

        using (var speechSynthesizer = new SpeechSynthesizer(speechConfig, audioOutputConfig))
        {
            var speechSynthesisResult = await speechSynthesizer.SpeakTextAsync(text).ConfigureAwait(true);

            if (speechSynthesisResult.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                Console.WriteLine($"Speech synthesized to speaker for text: [{text}]");
            }
            else if (speechSynthesisResult.Reason == ResultReason.Canceled)
            {
                var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(speechSynthesisResult);
                Console.WriteLine($"Speech synthesis canceled: {cancellationDetails.Reason}");

                if (cancellationDetails.Reason == CancellationReason.Error)
                {
                    Console.WriteLine($"Error details: {cancellationDetails.ErrorDetails}");
                }
            }
        }
    }

    // Continuously listens for speech input to recognize and send as text to Azure OpenAI
    async static Task ChatWithOpenAI()
    {
        // Should be the locale for the speaker's language.
        var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
        speechConfig.SpeechRecognitionLanguage = "ja-JP";

        using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
        using var speechRecognizer = new SpeechRecognizer(speechConfig, audioConfig);
        var conversationEnded = false;

        while (!conversationEnded)
        {
            Console.WriteLine("Azure OpenAI is listening. Say 'Stop' or press Ctrl-Z to end the conversation.");

            // Get audio from the microphone and then send it to the TTS service.
            var speechRecognitionResult = await speechRecognizer.RecognizeOnceAsync();

            switch (speechRecognitionResult.Reason)
            {
                case ResultReason.RecognizedSpeech:
                    if (speechRecognitionResult.Text == "ストップ。")
                    {
                        Console.WriteLine("Conversation ended.");
                        conversationEnded = true;
                    }
                    else
                    {
                        Console.WriteLine($"Recognized speech: {speechRecognitionResult.Text}");
                        await AskOpenAI(speechRecognitionResult.Text).ConfigureAwait(true);
                    }
                    break;
                case ResultReason.NoMatch:
                    Console.WriteLine($"No speech could be recognized: ");
                    break;
                case ResultReason.Canceled:
                    var cancellationDetails = CancellationDetails.FromResult(speechRecognitionResult);
                    Console.WriteLine($"Speech Recognition canceled: {cancellationDetails.Reason}");
                    if (cancellationDetails.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"Error details={cancellationDetails.ErrorDetails}");
                    }
                    break;
            }
        }
    }

    async static Task Main(string[] args)
    {
        try
        {
            await ChatWithOpenAI().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
using nexRemote.Desktop.Core.Interfaces;
using nexRemote.Shared.Models;
using nexRemote.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace nexRemote.Desktop.Core.Services
{
    public class ChatHostService : IChatClientService
    {
        private readonly IChatUiService _chatUiService;

        public ChatHostService(IChatUiService chatUiService)
        {
            _chatUiService = chatUiService;
        }

        private NamedPipeServerStream NamedPipeStream { get; set; }
        private StreamReader Reader { get; set; }
        private StreamWriter Writer { get; set; }

        public async Task StartChat(string requesterID, string organizationName)
        {
            NamedPipeStream = new NamedPipeServerStream("nexRemote_Chat" + requesterID, PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Writer = new StreamWriter(NamedPipeStream);
            Reader = new StreamReader(NamedPipeStream);

            var cts = new CancellationTokenSource(10000);
            try
            {
                await NamedPipeStream.WaitForConnectionAsync(cts.Token);
            }
            catch (TaskCanceledException)
            {
                Logger.Write("Próbowano nawiązać sesję czatu, ale klientowi nie udało się połączyć na czas.", Shared.Enums.EventType.Warning);
                Environment.Exit(0);
            }

            _chatUiService.ChatWindowClosed += OnChatWindowClosed;

            _chatUiService.ShowChatWindow(organizationName, Writer);

            _ = Task.Run(ReadFromStream);
        }

        private void OnChatWindowClosed(object sender, EventArgs e)
        {
            try
            {
                NamedPipeStream?.Dispose();
            }
            catch { }
        }

        private async Task ReadFromStream()
        {
            while (NamedPipeStream.IsConnected)
            {
                try
                {
                    var messageJson = await Reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(messageJson))
                    {
                        var chatMessage = JsonSerializer.Deserialize<ChatMessage>(messageJson);
                        _chatUiService.ReceiveChat(chatMessage);
                       
                    }
                }
                catch (Exception ex)
                {
                    Logger.Write(ex);
                }
            }
        }
    }
}

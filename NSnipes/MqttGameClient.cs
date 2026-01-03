using MQTTnet;
using MQTTnet.Client;
using System.Text;
using System.Text.Json;

namespace NSnipes;

public class MqttGameClient : IDisposable
{
    private IMqttClient? _mqttClient;
    private readonly MqttFactory _mqttFactory;
    private bool _isConnected = false;
    private string? _clientId;
    
    // Events
    public event Action<string, string>? OnMessageReceived; // topic, payload
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<string>? OnConnectionError; // error message
    
    // Broker configuration
    private const string DefaultBroker = "broker.hivemq.com";
    private const int DefaultPort = 1883;
    
    public bool IsConnected => _isConnected && _mqttClient?.IsConnected == true;
    
    public MqttGameClient()
    {
        _mqttFactory = new MqttFactory();
        _clientId = $"nsnipes_{Guid.NewGuid().ToString().Substring(0, 8)}";
    }
    
    public async Task<bool> ConnectAsync(string? broker = null, int? port = null)
    {
        try
        {
            _mqttClient = _mqttFactory.CreateMqttClient();
            
            // Set up message received handler
            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                var topic = e.ApplicationMessage.Topic;
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                OnMessageReceived?.Invoke(topic, payload);
                await Task.CompletedTask;
            };
            
            // Set up connection handlers
            _mqttClient.ConnectedAsync += async e =>
            {
                _isConnected = true;
                OnConnected?.Invoke();
                await Task.CompletedTask;
            };
            
            _mqttClient.DisconnectedAsync += async e =>
            {
                _isConnected = false;
                OnDisconnected?.Invoke();
                await Task.CompletedTask;
            };
            
            var options = new MqttClientOptionsBuilder()
                .WithClientId(_clientId)
                .WithTcpServer(broker ?? DefaultBroker, port ?? DefaultPort)
                .WithCleanSession()
                .Build();
            
            var result = await _mqttClient.ConnectAsync(options);
            
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                return true;
            }
            else
            {
                OnConnectionError?.Invoke($"Connection failed: {result.ResultCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Connection error: {ex.Message}");
            return false;
        }
    }
    
    public async Task DisconnectAsync()
    {
        if (_mqttClient != null && _mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync();
        }
    }
    
    public async Task<bool> SubscribeAsync(string topic, MQTTnet.Protocol.MqttQualityOfServiceLevel qos = MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
            return false;
        
        try
        {
            var subscribeOptions = _mqttFactory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(topic, qos)
                .Build();
            
            var result = await _mqttClient.SubscribeAsync(subscribeOptions);
            // In MQTTnet 4.x, check if subscription was successful
            // If no exception was thrown, assume success
            return true;
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Subscribe error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> PublishAsync(string topic, string payload, bool retain = false, MQTTnet.Protocol.MqttQualityOfServiceLevel qos = MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
            return false;
        
        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .WithQualityOfServiceLevel(qos)
                .Build();
            
            await _mqttClient.PublishAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Publish error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> PublishJsonAsync<T>(string topic, T payload, bool retain = false, MQTTnet.Protocol.MqttQualityOfServiceLevel qos = MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            return await PublishAsync(topic, json, retain, qos);
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"JSON publish error: {ex.Message}");
            return false;
        }
    }
    
    public void Dispose()
    {
        DisconnectAsync().Wait(1000);
        _mqttClient?.Dispose();
    }
}


namespace CleverBot.Agents;
public class CompositeAgentHostedService(IEnumerable<Agent> agents) : BackgroundService
{
    private readonly IReadOnlyCollection<Agent> _agents = agents.ToList();
    private readonly Dictionary<Agent, Task> _runningAgents = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var agent in _agents)
        {
            // Start each agent and monitor its lifecycle
            _runningAgents[agent] = MonitorAgentAsync(agent, stoppingToken);
        }

        // Wait for all agents to complete (if stoppingToken is canceled)
        await Task.WhenAll(_runningAgents.Values);
    }

    private async Task MonitorAgentAsync(Agent agent, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"Starting agent: {agent.AgentParameters.UserId}");
                await agent.StartAsync(stoppingToken);

                // Block until cancellation or failure
                await agent.WaitForCompletionAsync(stoppingToken); // Assuming your Agent has this method
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Agent {agent.AgentParameters.UserId} failed: {ex.Message}. Restarting...");
            }

            // Optional: Add a delay before restarting to avoid thrashing
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var agent in _agents)
        {
            try
            {
                Console.WriteLine($"Stopping agent: {agent.AgentParameters.UserId}");
                await agent.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to stop agent {agent.AgentParameters.UserId}: {ex.Message}");
            }
        }

        // Cancel all monitoring tasks
        cancellationToken.ThrowIfCancellationRequested();
    }
}
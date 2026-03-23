using Sentinel.Application.Abstractions;
using System.Threading.Channels;

namespace Sentinel.Infrastructure.DepsDev
{
    /// <summary>
    /// In-Memory Channel tabanlı lisans zenginleştirme kuyruğu.
    /// Bounded capacity ile RAM kullanımı kontrol altında tutulur.
    /// </summary>
    public class LicenseEnrichmentQueue : ILicenseEnrichmentQueue
    {
        private readonly Channel<Guid> _channel;

        public LicenseEnrichmentQueue()
        {
            // Bounded channel: Maksimum 500 bekleyen scan. Kapasite dolduğunda wait eder (backpressure).
            var options = new BoundedChannelOptions(500)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            _channel = Channel.CreateBounded<Guid>(options);
        }

        public ValueTask EnqueueScanAsync(Guid scanId, CancellationToken cancellationToken = default)
        {
            return _channel.Writer.WriteAsync(scanId, cancellationToken);
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}

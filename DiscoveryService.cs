using System;
using System.Collections.Generic;
using System.Text;
using Squared.Task;

namespace Tsunagaro {
    public class DiscoveryService {
        public readonly TaskScheduler Scheduler;

        public DiscoveryService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            yield break;
        }
    }
}

﻿using System.Collections.Generic;

namespace Shadowsocks.Controller.Strategy
{
    internal class StrategyManager
    {
        private readonly List<IStrategy> _strategies;

        public StrategyManager(ShadowsocksController controller)
        {
            _strategies = new List<IStrategy>();
            _strategies.Add(new BalancingStrategy(controller));
            _strategies.Add(new HighAvailabilityStrategy(controller));
            // TODO: load DLL plugins
        }

        public IList<IStrategy> GetStrategies()
        {
            return _strategies;
        }
    }
}
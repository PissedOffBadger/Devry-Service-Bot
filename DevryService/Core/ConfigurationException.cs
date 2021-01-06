﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevryService.Core
{
    public class ConfigurationException : Exception
    {
        public string Name { get; set; }

        public ConfigurationException(string name)
        {
            this.Name = name;
        }
    }
}

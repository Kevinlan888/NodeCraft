using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeCraft.Flow
{
    public class Parameter
    {
        public string ParameterType { get; set; }

        public object Value { get; set; }
    }

    public class PortParameter
    {
        public string PortId { get; set; }

        public string LinkId { get; set; }

        public EPortDirection PortDirection { get; set; }

        public bool IsDynamic { get; set; }

        public Parameter Parameter { get; set; }
    }
}

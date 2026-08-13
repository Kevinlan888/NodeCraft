using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeCraft.Flow
{
    public class ConnectorModel
    {
        public string ID { get; set; }

        public EIOType Type { get; set; }

        public EPortDirection Direction { get; set; }
    }

    public enum EIOType
    {
        Input,
        Output
    }

    public enum EPortDirection
    {
        None = -1,
        Top,
        Left,
        Right, 
        Bottom
    }
}

using System;
using System.Collections.Generic;

namespace Odengine.Fields
{
    /// <summary>
    /// Base class for all field types in Odengine.
    /// </summary>
    public abstract class Field
    {
        public abstract string FieldId { get; }
        public abstract FieldProfile Profile { get; }
        public abstract float GetAmplitude(string nodeId);
        public abstract IEnumerable<(string nodeId, float amplitude)> GetAllAmplitudes();
    }
}

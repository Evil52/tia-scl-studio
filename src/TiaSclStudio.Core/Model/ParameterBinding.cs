using System;

namespace TiaSclStudio.Core.Model
{
    public sealed class ParameterBinding
    {
        public ParameterBinding()
        {
            Id = Guid.NewGuid();
            ParameterName = string.Empty;
            Expression = string.Empty;
        }

        public ParameterBinding(InterfaceMember parameter, string expression)
            : this()
        {
            BindTo(parameter);
            Expression = expression ?? string.Empty;
        }

        public Guid Id { get; set; }

        /// <summary>Stable reference to an interface member. Preferred over ParameterName.</summary>
        public Guid ParameterId { get; set; }

        /// <summary>Readable fallback for hand-authored and legacy project files.</summary>
        public string ParameterName { get; set; }

        /// <summary>Raw SCL expression, for example "Motor_Start" or #temporaryValue.</summary>
        public string Expression { get; set; }

        public void BindTo(InterfaceMember parameter)
        {
            if (parameter == null)
            {
                throw new ArgumentNullException("parameter");
            }

            ParameterId = parameter.Id;
            ParameterName = parameter.Name;
        }
    }
}

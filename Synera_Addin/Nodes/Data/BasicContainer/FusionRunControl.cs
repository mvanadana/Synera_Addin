using Synera.Core.Implementation.Graph;
using Synera.Wpf.DirectX.Attributes;
using Synera.Wpf.DirectX.Canvas.Controls.Node;
using Synera.Wpf.DirectX.Canvas.Controls.NodeControls.Builders;
using Synera.Wpf.DirectX.Controls;
using Synera.Wpf.DirectX.Extensions;
using System.Windows.Controls;
using StackPanel = Synera.Wpf.DirectX.Controls.StackPanel;

namespace Synera_Addin.Nodes.Data.BasicContainer
{
    [ViewFor(typeof(FusionRun))]
    public sealed class FusionRunControl : NodeWithNodeControls
    {
        public FusionRunControl(FusionRun node)
            : base(node)
        {
            node.ParameterAdded += OnParametersChanged;
            node.ParameterRemoved += OnParametersChanged;
        }

        protected override void OnUnloaded()
        {
            base.OnUnloaded();
            Node.ParameterAdded -= OnParametersChanged;
            Node.ParameterRemoved -= OnParametersChanged;
        }

        protected override void ConfigureControls(StackPanel container, Builder builder)
        {
            // 🔒 Fixed inputs (Authentication, URL)
            var staticInputs = new StackPanel { Orientation = Orientation.Vertical };

            if (Node.InputParameters.Count > 0)
                staticInputs.AddChild(builder.CreateRegularInput(0)); // Authentication

            if (Node.InputParameters.Count > 1)
                staticInputs.AddChild(builder.CreateRegularInput(1)); // Fusion URL

            var expander = builder.CreateExpander(staticInputs, "Static Inputs")
                .WithText("General Settings")
                .Build();

            container.AddChild(expander);

            // 🔁 Dynamic user parameters
            var dynamicInputs = new StackPanel { Orientation = Orientation.Vertical };

            const int dynamicStartIndex = 2; // Update this if your FusionRun node changes its index definition

            for (int i = dynamicStartIndex; i < Node.InputParameters.Count; i++)
            {
                // 🛡️ Safety check
                if (i >= 0 && i < Node.InputParameters.Count)
                {
                    var paramControl = builder.CreateRegularInput(i);
                    dynamicInputs.AddChild(paramControl);
                }
            }


            var dynExpander = builder.CreateExpander(dynamicInputs, "User Parameters")
                .WithText("Fusion User Parameters")
                .Build();

            container.AddChild(dynExpander);
        }

        private void OnParametersChanged(object sender, Synera.Core.Events.ParameterEventArgs e)
        {
            Dispatcher.Invoke(InvalidateCustomContent); // Redraw the panel safely
        }
    }
}

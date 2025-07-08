using Synera.Core.Graph.Data;
using Synera.Core.Graph.Enums;
using Synera.Core.Implementation.Graph;
using Synera.Core.Implementation.UI;
using Synera.DataTypes;
using Synera.Kernels.DataTypes;
using Synera.Kernels.Geometry;
using Synera.Localization;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Synera_Addin.Nodes.Data.Import
{
    [Guid("4951a3c2-e4a0-4420-8d41-72f73d22c4f1")]
    public sealed class Fusion2 : Node
    {
        public const int Input1InputIndex = 0;
        public const int Output1OutputIndex = 0;

        public Fusion2()
            : base(new LocalizableString(nameof(Resources.Fusion2Name), typeof(Resources)))
        {
            Category = Categories.Data;
            Subcategory = Subcategories.Data.Import;
            Keywords = new LocalizableString(nameof(Resources.Fusion2Keywords), typeof(Resources));
            Description = new LocalizableString(nameof(Resources.Fusion2Description), typeof(Resources));
            GuiPriority = 1;
            CanBeVisible = true;
            IsReadonly = false;
            String InputName = "Si";
            String InputDescription = "Sm";

            InputParameterManager.AddParameter<SyneraString>(
                new LocalizableString(nameof(Resources.Fusion2Input1Name), typeof(Resources)),
                new LocalizableString(nameof(Resources.Fusion2Input1Description), typeof(Resources)),
                ParameterAccess.Item);

            InputParameterManager.AddParameter<IGraphDataType>(
   InputName,
   InputDescription,
   ParameterAccess.Item);

            OutputParameterManager.AddParameter<IGraphDataType>(
                new LocalizableString(nameof(Resources.Fusion2Output1Name), typeof(Resources)),
                new LocalizableString(nameof(Resources.Fusion2Output1Description), typeof(Resources)),
                ParameterAccess.Item);
        }

        protected override void SolveInstance(IDataAccess dataAccess)
        {
            var inputSuccess = true;
            inputSuccess &= dataAccess.GetData<SyneraString>(Input1InputIndex, out var input1);

            if (!inputSuccess)
                return;

            //throw new NotImplementedException();

            dataAccess.SetData(Output1OutputIndex, input1);
        }
    }
}

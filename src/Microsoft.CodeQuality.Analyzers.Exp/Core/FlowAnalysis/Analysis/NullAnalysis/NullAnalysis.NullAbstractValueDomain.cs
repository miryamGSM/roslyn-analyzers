﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.CodeAnalysis.Operations.DataFlow.NullAnalysis
{
    using NullAnalysisData = IDictionary<AnalysisEntity, NullAbstractValue>;

    internal partial class NullAnalysis : ForwardDataFlowAnalysis<NullAnalysisData, NullBlockAnalysisResult, NullAbstractValue>
    {
        /// <summary>
        /// Abstract value domain for <see cref="NullAnalysis"/> to merge and compare <see cref="NullAbstractValue"/> values.
        /// </summary>
        private class NullAbstractValueDomain : AbstractValueDomain<NullAbstractValue>
        {
            public static NullAbstractValueDomain Default = new NullAbstractValueDomain();

            private NullAbstractValueDomain() { }

            public override NullAbstractValue Bottom => NullAbstractValue.Undefined;

            public override NullAbstractValue UnknownOrMayBeValue => NullAbstractValue.MaybeNull;

            public override int Compare(NullAbstractValue oldValue, NullAbstractValue newValue)
            {
                return Comparer<NullAbstractValue>.Default.Compare(oldValue, newValue);
            }

            public override NullAbstractValue Merge(NullAbstractValue value1, NullAbstractValue value2)
            {
                NullAbstractValue result;

                if (value1 == NullAbstractValue.MaybeNull ||
                    value2 == NullAbstractValue.MaybeNull)
                {
                    result = NullAbstractValue.MaybeNull;
                }
                else if (value1 == NullAbstractValue.Undefined)
                {
                    result = value2;
                }
                else if (value2 == NullAbstractValue.Undefined)
                {
                    result = value1;
                }
                else if (value1 != value2)
                {
                    // One of the values must be 'Null' and other value must be 'NotNull'.
                    result = NullAbstractValue.MaybeNull;
                }
                else
                {
                    result = value1;
                }

                return result;
            }
        }
    }
}

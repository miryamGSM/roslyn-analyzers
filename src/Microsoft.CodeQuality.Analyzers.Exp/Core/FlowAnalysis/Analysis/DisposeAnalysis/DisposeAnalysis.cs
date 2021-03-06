﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Operations.ControlFlow;

namespace Microsoft.CodeAnalysis.Operations.DataFlow.DisposeAnalysis
{
    using DisposeAnalysisData = IDictionary<AbstractLocation, DisposeAbstractValue>;
    using DisposeAnalysisDomain = MapAbstractDomain<AbstractLocation, DisposeAbstractValue>;

    /// <summary>
    /// Dataflow analysis to track dispose state of <see cref="AbstractLocation"/>/<see cref="IOperation"/> instances.
    /// </summary>
    internal partial class DisposeAnalysis : ForwardDataFlowAnalysis<DisposeAnalysisData, DisposeBlockAnalysisResult, DisposeAbstractValue>
    {
        public static readonly DisposeAnalysisDomain DisposeAnalysisDomainInstance = new DisposeAnalysisDomain(DisposeAbstractValueDomain.Default);
        private DisposeAnalysis(DisposeAnalysisDomain analysisDomain, DisposeDataFlowOperationVisitor operationVisitor)
            : base(analysisDomain, operationVisitor)
        {
        }

        public static DataFlowAnalysisResult<DisposeBlockAnalysisResult, DisposeAbstractValue> GetOrComputeResult(
            ControlFlowGraph cfg,
            INamedTypeSymbol iDisposable,
            INamedTypeSymbol iCollection,
            INamedTypeSymbol genericICollection,
            ImmutableHashSet<INamedTypeSymbol> disposeOwnershipTransferLikelyTypes,
            INamedTypeSymbol containingTypeSymbol,
            DataFlowAnalysisResult<PointsToAnalysis.PointsToBlockAnalysisResult, PointsToAnalysis.PointsToAbstractValue> pointsToAnalysisResult,
            DataFlowAnalysisResult<NullAnalysis.NullBlockAnalysisResult, NullAnalysis.NullAbstractValue> nullAnalysisResultOpt = null)
        {
            Debug.Assert(cfg != null);
            Debug.Assert(iDisposable != null);
            Debug.Assert(containingTypeSymbol != null);
            Debug.Assert(pointsToAnalysisResult != null);

            var operationVisitor = new DisposeDataFlowOperationVisitor(iDisposable, iCollection, genericICollection,
                disposeOwnershipTransferLikelyTypes, DisposeAbstractValueDomain.Default, containingTypeSymbol, pointsToAnalysisResult, nullAnalysisResultOpt);
            var disposeAnalysis = new DisposeAnalysis(DisposeAnalysisDomainInstance, operationVisitor);
            return disposeAnalysis.GetOrComputeResultCore(cfg);
        }

        internal override DisposeBlockAnalysisResult ToResult(BasicBlock basicBlock, DataFlowAnalysisInfo<IDictionary<AbstractLocation, DisposeAbstractValue>> blockAnalysisData) => new DisposeBlockAnalysisResult(basicBlock, blockAnalysisData);
    }
}

﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis.Operations.DataFlow.NullAnalysis;
using Microsoft.CodeAnalysis.Operations.DataFlow.PointsToAnalysis;

namespace Microsoft.CodeAnalysis.Operations.DataFlow.DisposeAnalysis
{
    using DisposeAnalysisData = IDictionary<AbstractLocation, DisposeAbstractValue>;

    internal partial class DisposeAnalysis : ForwardDataFlowAnalysis<DisposeAnalysisData, DisposeBlockAnalysisResult, DisposeAbstractValue>
    {
        /// <summary>
        /// Operation visitor to flow the dispose values across a given statement in a basic block.
        /// </summary>
        private sealed class DisposeDataFlowOperationVisitor : AbstractLocationDataFlowOperationVisitor<DisposeAnalysisData, DisposeAbstractValue>
        {
            private readonly INamedTypeSymbol _iDisposable;
            private readonly INamedTypeSymbol _iCollection;
            private readonly INamedTypeSymbol _genericICollection;
            private readonly ImmutableHashSet<INamedTypeSymbol> _disposeOwnershipTransferLikelyTypes;

            public DisposeDataFlowOperationVisitor(
                INamedTypeSymbol iDisposable,
                INamedTypeSymbol iCollection,
                INamedTypeSymbol genericICollection,
                ImmutableHashSet<INamedTypeSymbol> disposeOwnershipTransferLikelyTypes,
                DisposeAbstractValueDomain valueDomain,
                INamedTypeSymbol containingTypeSymbol,
                DataFlowAnalysisResult<PointsToBlockAnalysisResult, PointsToAbstractValue> pointsToAnalysisResult,
                DataFlowAnalysisResult<NullBlockAnalysisResult, NullAbstractValue> nullAnalysisResultOpt)
                : base(valueDomain, containingTypeSymbol, nullAnalysisResultOpt: nullAnalysisResultOpt, pointsToAnalysisResultOpt: pointsToAnalysisResult)
            {
                Debug.Assert(iDisposable != null);
                Debug.Assert(disposeOwnershipTransferLikelyTypes != null);
                Debug.Assert(pointsToAnalysisResult != null);

                _iDisposable = iDisposable;
                _iCollection = iCollection;
                _genericICollection = genericICollection;
                _disposeOwnershipTransferLikelyTypes = disposeOwnershipTransferLikelyTypes;
            }

            protected override DisposeAbstractValue GetAbstractDefaultValue(ITypeSymbol type) => DisposeAbstractValue.NotDisposable;

            protected override DisposeAbstractValue GetAbstractValue(AbstractLocation location) => CurrentAnalysisData.TryGetValue(location, out var value) ? value : ValueDomain.UnknownOrMayBeValue;

            protected override void SetAbstractValue(AbstractLocation location, DisposeAbstractValue value)
            {
                Debug.Assert(location.LocationType.IsDisposable(_iDisposable));

                CurrentAnalysisData[location] = value;
            }

            protected override void SetAbstractValue(PointsToAbstractValue instanceLocation, DisposeAbstractValue value)
            {
                foreach (var location in instanceLocation.Locations)
                {
                    SetAbstractValue(location, value);
                }
            }

            protected override void ResetCurrentAnalysisData(DisposeAnalysisData newAnalysisDataOpt = null)
            {
                // Reset the current analysis data, while ensuring that we don't violate the monotonicity, i.e. we cannot remove any existing key from currentAnalysisData.
                if (newAnalysisDataOpt == null)
                {
                    // Just set the values for existing keys to ValueDomain.UnknownOrMayBeValue.
                    var keys = CurrentAnalysisData.Keys.ToImmutableArray();
                    foreach (var key in keys)
                    {
                        SetAbstractValue(key, ValueDomain.UnknownOrMayBeValue);
                    }
                }
                else
                {
                    // Merge the values from current and new analysis data.
                    var keys = CurrentAnalysisData.Keys.Concat(newAnalysisDataOpt.Keys).ToImmutableArray();
                    foreach (var key in keys)
                    {
                        var value1 = CurrentAnalysisData.TryGetValue(key, out var currentValue) ? currentValue : ValueDomain.Bottom;
                        var value2 = newAnalysisDataOpt.TryGetValue(key, out var newValue) ? newValue : ValueDomain.Bottom;
                        var mergedValue = ValueDomain.Merge(value1, value2);
                        SetAbstractValue(key, mergedValue);
                    }
                }
            }

            protected override DisposeAbstractValue HandleInstanceCreation(IOperation operation, PointsToAbstractValue instanceLocation, DisposeAbstractValue defaultValue)
            {
                if (!operation.Type.IsDisposable(_iDisposable))
                {
                    return defaultValue;
                }

                SetAbstractValue(instanceLocation, DisposeAbstractValue.NotDisposed);
                return DisposeAbstractValue.NotDisposed;
            }

            private void HandleDisposingOperation(IOperation disposingOperation, IOperation disposedInstance)
            {
                if (!disposedInstance.Type.IsDisposable(_iDisposable))
                {
                    return;
                }

                PointsToAbstractValue instanceLocation = GetPointsToAbstractValue(disposedInstance);
                foreach (AbstractLocation location in instanceLocation.Locations)
                {
                    if (CurrentAnalysisData.TryGetValue(location, out DisposeAbstractValue currentDisposeValue))
                    {
                        DisposeAbstractValue disposeValue = currentDisposeValue.WithNewDisposingOperation(disposingOperation);
                        SetAbstractValue(location, disposeValue);
                    }
                }
            }

            private void HandlePossibleEscapingOperation(IOperation escapingOperation, IOperation escapedInstance)
            {
                if (escapedInstance?.Type == null ||
                    !escapedInstance.Type.IsDisposable(_iDisposable))
                {
                    return;
                }

                PointsToAbstractValue instanceLocation = GetPointsToAbstractValue(escapedInstance);
                foreach (AbstractLocation location in instanceLocation.Locations)
                {
                    if (CurrentAnalysisData.TryGetValue(location, out DisposeAbstractValue currentDisposeValue))
                    {
                        DisposeAbstractValue newDisposeValue = currentDisposeValue.WithNewEscapingOperation(escapingOperation);
                        SetAbstractValue(location, newDisposeValue);
                    }
                }
            }

            private void HandlePossibleEscapingForAssignment(IOperation target, IOperation value, IOperation operation)
            {
                // FxCop compat: The object assigned to a field or a property or an array element is considered escaped.
                // TODO: Perform better analysis for array element assignments as we already track element locations.
                // https://github.com/dotnet/roslyn-analyzers/issues/1577
                if (target is IMemberReferenceOperation ||
                    target.Kind == OperationKind.ArrayElementReference)
                {
                    HandlePossibleEscapingOperation(operation, value);
                }
            }

            protected override void SetAbstractValueForElementInitializer(IOperation instance, ImmutableArray<AbstractIndex> indices, ITypeSymbol elementType, IOperation initializer, DisposeAbstractValue value)
            {
                HandlePossibleEscapingOperation(instance, initializer);
            }

            protected override void SetAbstractValueForSymbolDeclaration(ISymbol symbol, IOperation initializer, DisposeAbstractValue initializerValue)
            {
            }

            protected override void SetAbstractValueForAssignment(IOperation target, IOperation assignedValueOperation, DisposeAbstractValue assignedValue)
            {
                HandlePossibleEscapingForAssignment(target, assignedValueOperation, assignedValueOperation);
            }

            // TODO: Remove these temporary methods once we move to compiler's CFG
            // https://github.com/dotnet/roslyn-analyzers/issues/1567
            #region Temporary methods to workaround lack of *real* CFG
            protected override DisposeAnalysisData MergeAnalysisData(DisposeAnalysisData value1, DisposeAnalysisData value2)
                => DisposeAnalysisDomainInstance.Merge(value1, value2);
            protected override DisposeAnalysisData GetClonedAnalysisData()
                => new Dictionary<AbstractLocation, DisposeAbstractValue>(CurrentAnalysisData);
            protected override bool Equals(DisposeAnalysisData value1, DisposeAnalysisData value2)
                => EqualsHelper(value1, value2);
            #endregion

            #region Visitor methods
            public override DisposeAbstractValue DefaultVisit(IOperation operation, object argument)
            {
                var value = base.DefaultVisit(operation, argument);
                return DisposeAbstractValue.NotDisposable;
            }

            public override DisposeAbstractValue VisitInvocation_NonLambdaOrDelegateOrLocalFunction(IInvocationOperation operation, object argument)
            {
                var value = base.VisitInvocation_NonLambdaOrDelegateOrLocalFunction(operation, argument);
                var disposeMethodKind = operation.TargetMethod.GetDisposeMethodKind(_iDisposable);
                switch (disposeMethodKind)
                {
                    case DisposeMethodKind.Dispose:
                    case DisposeMethodKind.DisposeBool:
                        HandleDisposingOperation(operation, operation.Instance);
                        break;

                    case DisposeMethodKind.Close:
                        // FxCop compat: Calling "this.Close" shouldn't count as disposing the object within the implementation of Dispose.
                        if (!(operation.Instance is IInstanceReferenceOperation))
                        {
                            goto case DisposeMethodKind.Dispose;
                        }
                        break;

                    default:
                        // FxCop compat: Catches things like static calls to File.Open() and Create()
                        if (operation.TargetMethod.IsStatic &&
                            (operation.TargetMethod.Name.StartsWith("create", StringComparison.OrdinalIgnoreCase) ||
                             operation.TargetMethod.Name.StartsWith("open", StringComparison.OrdinalIgnoreCase)))
                        {
                            var instanceLocation = GetPointsToAbstractValue(operation);
                            Debug.Assert(instanceLocation.Kind == PointsToAbstractValueKind.Known);
                            return HandleInstanceCreation(operation, instanceLocation, value);
                        }
                        else if (operation.Arguments.Length > 0 &&
                            (operation.TargetMethod.IsCollectionAddMethod(_iCollection) ||
                             operation.TargetMethod.IsCollectionAddMethod(_genericICollection)))
                        {
                            // FxCop compat: The object added to a collection is considered escaped.
                            var lastArgument = operation.Arguments[operation.Arguments.Length - 1];
                            HandlePossibleEscapingOperation(operation, lastArgument.Value);
                        }

                        break;
                }

                return value;
            }

            protected override DisposeAbstractValue VisitAssignmentOperation(IAssignmentOperation operation, object argument)
            {
                var value = base.VisitAssignmentOperation(operation, argument);
                HandlePossibleEscapingForAssignment(operation.Target, operation.Value, operation);
                return value;
            }

            public override DisposeAbstractValue VisitArgument(IArgumentOperation operation, object argument)
            {
                var value = base.VisitArgument(operation, argument);

                // Discover if a disposable object is being passed into the creation method for this new disposable object
                // and if the new dispoable object assumes ownership of that passed in disposable object.
                if (operation.Parent is IObjectCreationOperation objectCreation &&
                    objectCreation.Arguments.Length == 1 &&
                    objectCreation.Constructor.Parameters.Length == 1 &&
                    _disposeOwnershipTransferLikelyTypes.Contains(objectCreation.Constructor.Parameters[0].Type))
                {
                    HandlePossibleEscapingOperation(operation, operation.Value);
                }
                else if (operation.Parameter.RefKind != RefKind.None)
                {
                    // Argument passed by ref/out is considered escaped.
                    HandlePossibleEscapingOperation(operation, operation.Value);
                }

                return value;
            }

            public override DisposeAbstractValue VisitReturn(IReturnOperation operation, object argument)
            {
                var value = base.VisitReturn(operation, argument);
                HandlePossibleEscapingOperation(operation, operation.ReturnedValue);
                return value;
            }

            public override DisposeAbstractValue VisitUsing(IUsingOperation operation, object argument)
            {
                var value = base.VisitUsing(operation, argument);
                if (operation.Resources is IVariableDeclarationGroupOperation varDeclGroup)
                {
                    var variableDeclarationInitializers = varDeclGroup.Declarations.Select(declaration => declaration.Initializer?.Value).WhereNotNull();
                    var variableDeclaratorInitializers = varDeclGroup.Declarations.SelectMany(declaration => declaration.Declarators).Select(declarator => declarator.Initializer?.Value).WhereNotNull();
                    foreach (var disposedInstance in variableDeclarationInitializers.Concat(variableDeclaratorInitializers))
                    {
                        HandleDisposingOperation(operation, disposedInstance);
                    }
                }
                else if (operation.Resources != null)
                {
                    HandleDisposingOperation(operation, operation.Resources);
                }

                return value;
            }

            public override DisposeAbstractValue VisitConversion(IConversionOperation operation, object argument)
            {
                var value = base.VisitConversion(operation, argument);
                if (operation.OperatorMethod != null)
                {
                    // Conservatively handle user defined conversions.
                    HandlePossibleEscapingOperation(operation, operation.Operand);
                }

                return value;
            }

            #endregion
        }
    }
}

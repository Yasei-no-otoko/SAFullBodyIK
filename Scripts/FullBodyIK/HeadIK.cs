﻿// Copyright (c) 2016 Nora
// Released under the MIT license
// http://opensource.org/licenses/mit-license.phpusing
using UnityEngine;

namespace SA
{
	public partial class FullBodyIK
	{
		public class HeadIK
		{
			Settings _settings;
			InternalValues _internalValues;

			Bone _neckBone;
			Bone _headBone;
			Bone _leftEyeBone;
			Bone _rightEyeBone;

			Effector _neckEffector;
			Effector _headEffector;
			Effector _eyesEffector;

			// for UnityChan
			Vector3 _headBoneLossyScale = Vector3.one;
			bool _isHeadBoneLossyScaleFuzzyIdentity = true;

			Quaternion _headEffectorToWorldRotation = Quaternion.identity;
			Quaternion _headToLeftEyeRotation = Quaternion.identity;
			Quaternion _headToRightEyeRotation = Quaternion.identity;

			public HeadIK( FullBodyIK fullBodyIK )
			{
				_settings = fullBodyIK.settings;
				_internalValues = fullBodyIK.internalValues;

				_neckBone = _PrepareBone( fullBodyIK.headBones.neck );
				_headBone = _PrepareBone( fullBodyIK.headBones.head );
				_leftEyeBone = _PrepareBone( fullBodyIK.headBones.leftEye );
				_rightEyeBone = _PrepareBone( fullBodyIK.headBones.rightEye );
				_neckEffector = fullBodyIK.headEffectors.neck;
				_headEffector = fullBodyIK.headEffectors.head;
				_eyesEffector = fullBodyIK.headEffectors.eyes;

				_Prepare();
            }

			public static Vector3 _unityChan_leftEyeDefaultLocalPosition = new Vector3( -0.042531f + 0.024f, 0.048524f, 0.047682f - 0.02f );
			public static Vector3 _unityChan_rightEyeDefaultLocalPosition = new Vector3( 0.042531f - 0.024f, 0.048524f, 0.047682f - 0.02f );
			Vector3 _unityChan_leftEyeDefaultPosition = Vector3.zero;
			Vector3 _unityChan_rightEyeDefaultPosition = Vector3.zero;

			void _Prepare()
			{
				if( _headBone != null ) {
					_headEffectorToWorldRotation = Inverse( _headEffector.defaultRotation ) * _headBone._defaultRotation;
					if( _leftEyeBone != null ) {
						_headToLeftEyeRotation = Inverse( _headBone._defaultRotation ) * _leftEyeBone._defaultRotation;
                    }
					if( _rightEyeBone != null ) {
						_headToRightEyeRotation = Inverse( _headBone._defaultRotation ) * _rightEyeBone._defaultRotation;
					}
				}

				if( _settings.modelTemplate == ModelTemplate.UnityChan ) {
					if( _headBone != null && _headBone.transformIsAlive ) {
						Vector3 leftPos, rightPos;
						SAFBIKMatMultVec( out leftPos, ref _internalValues.defaultRootBasis, ref _unityChan_leftEyeDefaultLocalPosition );
						SAFBIKMatMultVec( out rightPos, ref _internalValues.defaultRootBasis,  ref _unityChan_rightEyeDefaultLocalPosition );

						_headBoneLossyScale = _headBone.transform.lossyScale;
						_isHeadBoneLossyScaleFuzzyIdentity = IsFuzzy( _headBoneLossyScale, Vector3.one );

						if( !_isHeadBoneLossyScaleFuzzyIdentity ) {
							leftPos = Scale( ref leftPos, ref _headBoneLossyScale );
							rightPos = Scale( ref rightPos, ref _headBoneLossyScale );
						}

						_unityChan_leftEyeDefaultPosition = _headBone._defaultPosition + leftPos;
						_unityChan_rightEyeDefaultPosition = _headBone._defaultPosition + rightPos;
					}
				}
			}

			public bool Solve()
			{
				if( _neckBone == null || !_neckBone.transformIsAlive ||
					_headBone == null || !_headBone.transformIsAlive ||
					_headBone.parentBone == null || !_headBone.parentBone.transformIsAlive ) {
					return false;
				}

				float headPositionWeight = _headEffector.positionEnabled ? _headEffector.positionWeight : 0.0f;
				float eyesPositionWeight = _eyesEffector.positionEnabled ? _eyesEffector.positionWeight : 0.0f;

				if( headPositionWeight <= IKEpsilon && eyesPositionWeight <= IKEpsilon ) {
					Quaternion parentWorldRotation = _neckBone.parentBone.worldRotation;
					Quaternion parentBaseRotation = parentWorldRotation * _neckBone.parentBone._worldToBaseRotation;

					if( _internalValues.resetTransforms ) {
						_neckBone.worldRotation = parentBaseRotation * _neckBone._baseToWorldRotation;
					}

					float headRotationWeight = _headEffector.rotationEnabled ? _headEffector.rotationWeight : 0.0f;
					if( headRotationWeight > IKEpsilon ) {
						Quaternion toRotation = _headEffector.worldRotation * _headEffectorToWorldRotation;
						if( headRotationWeight < 1.0f - IKEpsilon ) {
							Quaternion fromRotation;
							if( _internalValues.resetTransforms ) {
								fromRotation = parentBaseRotation * _headBone._baseToWorldRotation;
							} else {
								fromRotation = _headBone.worldRotation; // This is able to use _headBone.worldRotation directly.
							}
							_headBone.worldRotation = Quaternion.Lerp( fromRotation, toRotation, headRotationWeight );
						} else {
							_headBone.worldRotation = toRotation;
						}

						_HeadRotationLimit();
					} else {
						if( _internalValues.resetTransforms ) {
							_headBone.worldRotation = parentBaseRotation * _headBone._baseToWorldRotation;
						}
					}

					if( _internalValues.resetTransforms ) {
						if( _settings.modelTemplate == ModelTemplate.UnityChan ) {
							_ResetEyesUnityChan();
						} else {
							_ResetEyes();
						}
					}

					return _internalValues.resetTransforms || (headRotationWeight > IKEpsilon);
				}

				_Solve();
				return true;
			}

			void _HeadRotationLimit()
			{
				// Rotation Limit.
				Quaternion headRotation = _headBone.worldRotation * _headBone._worldToBaseRotation;
				Quaternion neckRotation = _neckBone.worldRotation * _neckBone._worldToBaseRotation;
				Quaternion localRotation = Inverse( neckRotation ) * headRotation;
				Matrix3x3 localBasis;
				SAFBIKMatSetRot( out localBasis, ref localRotation );

				float headLimitX = SAFBIKSin( 10.0f * Mathf.Deg2Rad );
				float headLimitZPlus = SAFBIKSin( 5.0f * Mathf.Deg2Rad );
				float headLimitZMinus = SAFBIKSin( 5.0f * Mathf.Deg2Rad );

				Vector3 localDirY = localBasis.column1;
				Vector3 localDirZ = localBasis.column2;

				bool isLimited = false;
				isLimited |= _LimitXZ_Square( ref localDirY,
					_internalValues.headIK.headLimitRollTheta.sin,
					_internalValues.headIK.headLimitRollTheta.sin,
					_internalValues.headIK.headLimitPitchUpTheta.sin,
                    _internalValues.headIK.headLimitPitchDownTheta.sin );
				isLimited |= _LimitXY_Square( ref localDirZ,
					_internalValues.headIK.headLimitYawTheta.sin,
					_internalValues.headIK.headLimitYawTheta.sin,
					_internalValues.headIK.headLimitPitchDownTheta.sin,
					_internalValues.headIK.headLimitPitchUpTheta.sin );

				if( isLimited ) {
					if( SAFBIKComputeBasisFromYZLockZ( out localBasis, ref localDirY, ref localDirZ ) ) {
						SAFBIKMatGetRot( out localRotation, ref localBasis );
						headRotation = neckRotation * localRotation;
						headRotation = Normalize( headRotation * _headBone._baseToWorldRotation );
						_headBone.worldRotation = headRotation;
					}
				}
			}

			void _Solve()
			{
				Quaternion parentWorldRotation = _neckBone.parentBone.worldRotation;
				Matrix3x3 parentBasis;
				SAFBIKMatSetRotMultInv1( out parentBasis, ref parentWorldRotation, ref _neckBone.parentBone._defaultRotation );
				Matrix3x3 parentBaseBasis;
				SAFBIKMatMult( out parentBaseBasis, ref parentBasis, ref _internalValues.defaultRootBasis );
				Quaternion parentBaseRotation = parentWorldRotation * _neckBone.parentBone._worldToBaseRotation;

				float headPositionWeight = _headEffector.positionEnabled ? _headEffector.positionWeight : 0.0f;
				float eyesPositionWeight = _eyesEffector.positionEnabled ? _eyesEffector.positionWeight : 0.0f;

				Quaternion neckBonePrevRotation = Quaternion.identity;
				Quaternion headBonePrevRotation = Quaternion.identity;
				Quaternion leftEyeBonePrevRotation = Quaternion.identity;
				Quaternion rightEyeBonePrevRotation = Quaternion.identity;
				if( !_internalValues.resetTransforms ) {
					neckBonePrevRotation = _neckBone.worldRotation;
					headBonePrevRotation = _headBone.worldRotation;
					if( _leftEyeBone != null && _leftEyeBone.transformIsAlive ) {
						leftEyeBonePrevRotation = _leftEyeBone.worldRotation;
					}
					if( _rightEyeBone != null && _rightEyeBone.transformIsAlive ) {
						rightEyeBonePrevRotation = _rightEyeBone.worldRotation;
					}
				}

				// for Neck
				if( headPositionWeight > IKEpsilon ) {
					Matrix3x3 neckBoneBasis;
					SAFBIKMatMult( out neckBoneBasis, ref parentBasis, ref _neckBone._localAxisBasis );

					Vector3 yDir = _headEffector.worldPosition - _neckBone.worldPosition; // Not use _hidden_worldPosition
					if( SAFBIKVecNormalize( ref yDir ) ) {
						Vector3 localDir;
						SAFBIKMatMultVecInv( out localDir, ref neckBoneBasis, ref yDir );

						if( _LimitXZ_Square( ref localDir,
							_internalValues.headIK.neckLimitRollTheta.sin,
							_internalValues.headIK.neckLimitRollTheta.sin,
							_internalValues.headIK.neckLimitPitchDownTheta.sin,
							_internalValues.headIK.neckLimitPitchUpTheta.sin ) ) {
							SAFBIKMatMultVec( out yDir, ref neckBoneBasis, ref localDir );
						}

						Vector3 xDir = parentBaseBasis.column0;
						Vector3 zDir = parentBaseBasis.column2;
						if( SAFBIKComputeBasisLockY( out neckBoneBasis, ref xDir, ref yDir, ref zDir ) ) {
							Quaternion worldRotation;
							SAFBIKMatMultGetRot( out worldRotation, ref neckBoneBasis, ref _neckBone._boneToWorldBasis );
							if( headPositionWeight < 1.0f - IKEpsilon ) {
								Quaternion fromRotation;
								if( _internalValues.resetTransforms ) {
									fromRotation = parentBaseRotation * _neckBone._baseToWorldRotation;
								} else {
									fromRotation = neckBonePrevRotation; // This is able to use _headBone.worldRotation directly.
								}

								_neckBone.worldRotation = Quaternion.Lerp( fromRotation, worldRotation, headPositionWeight );
                            } else {
								_neckBone.worldRotation = worldRotation;
							}
						}
					}
				} else if( _internalValues.resetTransforms ) {
					_neckBone.worldRotation = parentBaseRotation * _neckBone._baseToWorldRotation;
				}

				// for Head / Eyes
				if( eyesPositionWeight <= IKEpsilon ) {
					float headRotationWeight = _headEffector.rotationEnabled ? _headEffector.rotationWeight : 0.0f;
					if( headRotationWeight > IKEpsilon ) {
						Quaternion toRotation = _headEffector.worldRotation * _headEffectorToWorldRotation;
						if( headRotationWeight < 1.0f - IKEpsilon ) {
							Quaternion fromRotation;
							if( _internalValues.resetTransforms ) {
								Quaternion neckBaseRotation = _neckBone.worldRotation * _neckBone._worldToBaseRotation;
								fromRotation = neckBaseRotation * _headBone._baseToWorldRotation;
							} else {
								// Not use _headBone.worldRotation.
								fromRotation = Normalize( _neckBone.worldRotation * Inverse( neckBonePrevRotation ) * headBonePrevRotation );
							}
							_headBone.worldRotation = Quaternion.Lerp( fromRotation, toRotation, headRotationWeight );
						} else {
							_headBone.worldRotation = toRotation;
						}
					} else {
						if( _internalValues.resetTransforms ) {
							Quaternion neckBaseRotation = _neckBone.worldRotation * _neckBone._worldToBaseRotation;
							_headBone.worldRotation = neckBaseRotation * _headBone._baseToWorldRotation;
						}
					}

					_HeadRotationLimit();

					if( _internalValues.resetTransforms ) {
						if( _settings.modelTemplate == ModelTemplate.UnityChan ) {
							_ResetEyesUnityChan();
						} else {
							_ResetEyes();
						}
					}

					return;
				}

				{
					Vector3 eyesPosition, parentBoneWorldPosition = _neckBone.parentBone.worldPosition;
					SAFBIKMatMultVecPreSubAdd( out eyesPosition, ref parentBasis, ref _eyesEffector._defaultPosition, ref _neckBone.parentBone._defaultPosition, ref parentBoneWorldPosition );

					// Note: Not use _eyesEffector._hidden_worldPosition
					Vector3 eyesDir = _eyesEffector.worldPosition - eyesPosition; // Memo: Not normalize yet.

					Matrix3x3 neckBaseBasis = parentBaseBasis;

					{
						Vector3 localDir;
						SAFBIKMatMultVecInv( out localDir, ref parentBaseBasis, ref eyesDir );

						localDir.y *= _settings.headIK.eyesToNeckPitchRate;
						SAFBIKVecNormalize( ref localDir );

						if( _ComputeEyesRange( ref localDir, _internalValues.headIK.eyesRangeTheta.cos ) ) {
							if( localDir.y < -_internalValues.headIK.neckLimitPitchDownTheta.sin ) {
								localDir.y = -_internalValues.headIK.neckLimitPitchDownTheta.sin;
							} else if( localDir.y > _internalValues.headIK.neckLimitPitchUpTheta.sin ) {
								localDir.y = _internalValues.headIK.neckLimitPitchUpTheta.sin;
							}
							localDir.x = 0.0f;
							localDir.z = SAFBIKSqrt( 1.0f - localDir.y * localDir.y );
						}

						SAFBIKMatMultVec( out eyesDir, ref parentBaseBasis, ref localDir );

						{
							Vector3 xDir = parentBaseBasis.column0;
							Vector3 yDir = parentBaseBasis.column1;
							Vector3 zDir = eyesDir;

							if( !SAFBIKComputeBasisLockZ( out neckBaseBasis, ref xDir, ref yDir, ref zDir ) ) {
								neckBaseBasis = parentBaseBasis; // Failsafe.
                            }
						}

						Quaternion worldRotation;
						SAFBIKMatMultGetRot( out worldRotation, ref neckBaseBasis, ref _neckBone._baseToWorldBasis );
						if( _eyesEffector.positionWeight < 1.0f - IKEpsilon ) {
							Quaternion neckWorldRotation = Quaternion.Lerp( _neckBone.worldRotation, worldRotation, _eyesEffector.positionWeight ); // This is able to use _neckBone.worldRotation directly.
							_neckBone.worldRotation = neckWorldRotation;
                            SAFBIKMatSetRotMult( out neckBaseBasis, ref neckWorldRotation, ref _neckBone._worldToBaseRotation );
						} else {
							_neckBone.worldRotation = worldRotation;
						}
                    }

					Matrix3x3 neckBasis;
					SAFBIKMatMult( out neckBasis, ref neckBaseBasis, ref _internalValues.defaultRootBasisInv );

					Vector3 neckBoneWorldPosition = _neckBone.worldPosition;
                    SAFBIKMatMultVecPreSubAdd( out eyesPosition, ref neckBasis, ref _eyesEffector._defaultPosition, ref _neckBone._defaultPosition, ref neckBoneWorldPosition );

					// Note: Not use _eyesEffector._hidden_worldPosition
					eyesDir = _eyesEffector.worldPosition - eyesPosition;

					Matrix3x3 headBaseBasis = neckBaseBasis;

					{
						Vector3 localDir;
						SAFBIKMatMultVecInv( out localDir, ref neckBaseBasis, ref eyesDir );

						localDir.x *= _settings.headIK.eyesToHeadYawRate;
						localDir.y *= _settings.headIK.eyesToHeadPitchRate;

						SAFBIKVecNormalize( ref localDir );

						if( _ComputeEyesRange( ref localDir, _internalValues.headIK.eyesRangeTheta.cos ) ) {
							// Note: Not use _LimitXY() for Stability
							_LimitXY_Square( ref localDir,
								_internalValues.headIK.headLimitYawTheta.sin,
								_internalValues.headIK.headLimitYawTheta.sin,
								_internalValues.headIK.headLimitPitchDownTheta.sin,
								_internalValues.headIK.headLimitPitchUpTheta.sin );
						}
						
						SAFBIKMatMultVec( out eyesDir, ref neckBaseBasis, ref localDir );

						{
							Vector3 xDir = neckBaseBasis.column0;
							Vector3 yDir = neckBaseBasis.column1;
							Vector3 zDir = eyesDir;

							if( !SAFBIKComputeBasisLockZ( out headBaseBasis, ref xDir, ref yDir, ref zDir ) ) {
								headBaseBasis = neckBaseBasis;
							}
						}

						Quaternion worldRotation;
						SAFBIKMatMultGetRot( out worldRotation, ref headBaseBasis, ref _headBone._baseToWorldBasis );
						if( _eyesEffector.positionWeight < 1.0f - IKEpsilon ) {
							Quaternion headFromWorldRotation = Normalize( _neckBone.worldRotation * Inverse( neckBonePrevRotation ) * headBonePrevRotation );
							Quaternion headWorldRotation = Quaternion.Lerp( headFromWorldRotation, worldRotation, _eyesEffector.positionWeight );
							_headBone.worldRotation = headWorldRotation;
							SAFBIKMatSetRotMult( out headBaseBasis, ref headWorldRotation, ref _headBone._worldToBaseRotation );
						} else {
							_headBone.worldRotation = worldRotation;
						}
					}

					Matrix3x3 headBasis;
					SAFBIKMatMult( out headBasis, ref headBaseBasis, ref _internalValues.defaultRootBasisInv );

					if( _settings.modelTemplate == ModelTemplate.UnityChan ) {
						_SolveEyesUnityChan( ref neckBasis, ref headBasis, ref headBaseBasis );
                    } else {
						_SolveEyes( ref neckBasis, ref headBasis, ref headBaseBasis, ref headBonePrevRotation, ref leftEyeBonePrevRotation, ref rightEyeBonePrevRotation );
					}
				}
			}

			void _ResetEyesUnityChan()
			{
				Vector3 headWorldPosition = _headBone.worldPosition;
				Quaternion headWorldRotation = _headBone.worldRotation;
				Matrix3x3 headBasis;
				SAFBIKMatSetRotMultInv1( out headBasis, ref headWorldRotation, ref _headBone._defaultRotation );

				Vector3 worldPotision;
				if( _leftEyeBone != null && _leftEyeBone.transformIsAlive ) {
					SAFBIKMatMultVecPreSubAdd( out worldPotision, ref headBasis, ref _leftEyeBone._defaultPosition, ref _headBone._defaultPosition, ref headWorldPosition );
					_leftEyeBone.worldPosition = worldPotision;
					_leftEyeBone.worldRotation = headWorldRotation * _headToLeftEyeRotation;
				}
				if( _rightEyeBone != null && _rightEyeBone.transformIsAlive ) {
					SAFBIKMatMultVecPreSubAdd( out worldPotision, ref headBasis, ref _rightEyeBone._defaultPosition, ref _headBone._defaultPosition, ref headWorldPosition );
					_rightEyeBone.worldPosition = worldPotision;
					_rightEyeBone.worldRotation = headWorldRotation * _headToRightEyeRotation;
				}
			}

			void _SolveEyesUnityChan( ref Matrix3x3 neckBasis, ref Matrix3x3 headBasis, ref Matrix3x3 headBaseBasis )
			{
				if( (_leftEyeBone != null && _leftEyeBone.transformIsAlive) || (_rightEyeBone != null && _rightEyeBone.transformIsAlive) ) {
					Vector3 leftEyePosition = new Vector3( -0.042531f, 0.048524f, 0.047682f );
					Vector3 rightEyePosition = new Vector3( 0.042531f, 0.048524f, 0.047682f );

					float _eyesHorzLimitAngle = 40.0f;
					float _eyesVertLimitAngle = 4.5f;
					float _eyesXRate = 0.796f;
					float _eyesYRate = 0.28f;
					float _eyesOuterXRotRate = 0.096f;
					float _eyesInnerXRotRate = 0.065f;
					float _eyesXOffset = -0.024f;
					float _eyesYOffset = 0.0f;
					float _eyesZOffset = -0.02f;

					_internalValues.UpdateDebugValue( "_eyesHorzLimitAngle", ref _eyesHorzLimitAngle );
					_internalValues.UpdateDebugValue( "_eyesVertLimitAngle", ref _eyesVertLimitAngle );
					_internalValues.UpdateDebugValue( "_eyesXRate", ref _eyesXRate );
					_internalValues.UpdateDebugValue( "_eyesYRate", ref _eyesYRate );
					_internalValues.UpdateDebugValue( "_eyesOuterXRotRate", ref _eyesOuterXRotRate );
					_internalValues.UpdateDebugValue( "_eyesInnerXRotRate", ref _eyesInnerXRotRate );
					_internalValues.UpdateDebugValue( "_eyesXOffset", ref _eyesXOffset );
					_internalValues.UpdateDebugValue( "_eyesYOffset", ref _eyesYOffset );
					_internalValues.UpdateDebugValue( "_eyesZOffset", ref _eyesZOffset );

					float _innerMoveXRate = 0.063f;
					float _outerMoveXRate = 0.063f;

					_internalValues.UpdateDebugValue( "_innerMoveXRate", ref _innerMoveXRate );
					_internalValues.UpdateDebugValue( "_outerMoveXRate", ref _outerMoveXRate );

					_innerMoveXRate *= 0.1f;
					_outerMoveXRate *= 0.1f;

					_eyesHorzLimitAngle *= Mathf.Deg2Rad;
					_eyesVertLimitAngle *= Mathf.Deg2Rad;

					float _eyesHorzLimit = Mathf.Sin( _eyesHorzLimitAngle );
					float _eyesVertLimit = Mathf.Sin( _eyesVertLimitAngle );

					leftEyePosition.x -= _eyesXOffset;
					rightEyePosition.x += _eyesXOffset;
					leftEyePosition.y += _eyesYOffset;
					rightEyePosition.y += _eyesYOffset;
					leftEyePosition.z += _eyesZOffset;
					rightEyePosition.z += _eyesZOffset;

					Vector3 leftEyeDefaultPosition = _unityChan_leftEyeDefaultPosition;
					Vector3 rightEyeDefaultPosition = _unityChan_rightEyeDefaultPosition;
					leftEyePosition = _unityChan_leftEyeDefaultPosition;
					rightEyePosition = _unityChan_rightEyeDefaultPosition;

					Vector3 headWorldPosition, neckBoneWorldPosition = _neckBone.worldPosition;
					SAFBIKMatMultVecPreSubAdd( out headWorldPosition, ref neckBasis, ref _headBone._defaultPosition, ref _neckBone._defaultPosition, ref neckBoneWorldPosition );
					Vector3 eyesPosition;
					SAFBIKMatMultVecPreSubAdd( out eyesPosition, ref headBasis, ref _eyesEffector._defaultPosition, ref _headBone._defaultPosition, ref headWorldPosition );

					Vector3 eyesDir = _eyesEffector.worldPosition - eyesPosition;

					Matrix3x3 leftEyeBaseBasis = headBaseBasis;
					Matrix3x3 rightEyeBaseBasis = headBaseBasis;

					SAFBIKMatMultVecInv( out eyesDir, ref headBaseBasis, ref eyesDir );

					SAFBIKVecNormalize( ref eyesDir );

					if( _eyesEffector.positionWeight < 1.0f - IKEpsilon ) {
						Vector3 tempDir = Vector3.Lerp( new Vector3( 0.0f, 0.0f, 1.0f ), eyesDir, _eyesEffector.positionWeight );
						if( SAFBIKVecNormalize( ref tempDir ) ) {
							eyesDir = tempDir;
						}
					}

					_LimitXY_Square( ref eyesDir,
						Mathf.Sin( _eyesHorzLimitAngle ),
						Mathf.Sin( _eyesHorzLimitAngle ),
						Mathf.Sin( _eyesVertLimitAngle ),
						Mathf.Sin( _eyesVertLimitAngle ) );

					float moveX = Mathf.Clamp( eyesDir.x * _eyesXRate, -_eyesHorzLimit, _eyesHorzLimit );
					float moveY = Mathf.Clamp( eyesDir.y * _eyesYRate, -_eyesVertLimit, _eyesVertLimit );
					float moveZ = -Mathf.Max( 1.0f - eyesDir.z, 1.0f - _eyesVertLimit ); // Reuse _eyesVertLimit.

					eyesDir.x *= _eyesXRate;
					eyesDir.y *= _eyesYRate;
					Vector3 leftEyeDir = eyesDir;
					Vector3 rightEyeDir = eyesDir;

					if( eyesDir.x >= 0.0f ) {
						leftEyeDir.x *= _eyesInnerXRotRate;
						rightEyeDir.x *= _eyesOuterXRotRate;
					} else {
						leftEyeDir.x *= _eyesOuterXRotRate;
						rightEyeDir.x *= _eyesInnerXRotRate;
					}

					SAFBIKVecNormalize2( ref leftEyeDir, ref rightEyeDir );

					SAFBIKMatMultVec( out leftEyeDir, ref headBaseBasis, ref leftEyeDir );
					SAFBIKMatMultVec( out rightEyeDir, ref headBaseBasis, ref rightEyeDir );

					float leftXRate = (moveX >= 0.0f) ? _innerMoveXRate : _outerMoveXRate;
					float rightXRate = (moveX >= 0.0f) ? _outerMoveXRate : _innerMoveXRate;

					{
						Vector3 xDir = headBasis.column0;
						Vector3 yDir = headBasis.column1;
						Vector3 zDir = leftEyeDir;
						SAFBIKComputeBasisLockZ( out leftEyeBaseBasis, ref xDir, ref yDir, ref zDir );
					}

					{
						Vector3 xDir = headBasis.column0;
						Vector3 yDir = headBasis.column1;
						Vector3 zDir = rightEyeDir;
						SAFBIKComputeBasisLockZ( out rightEyeBaseBasis, ref xDir, ref yDir, ref zDir );
					}

					Vector3 leftEyeWorldPosition;
					Vector3 rightEyeWorldPosition;

					leftEyeWorldPosition = headBaseBasis.column0 * (leftXRate * moveX);
					rightEyeWorldPosition = headBaseBasis.column0 * (rightXRate * moveX);

					if( !_isHeadBoneLossyScaleFuzzyIdentity ) {
						leftEyeWorldPosition = Scale( ref leftEyeWorldPosition, ref _headBoneLossyScale );
						rightEyeWorldPosition = Scale( ref rightEyeWorldPosition, ref _headBoneLossyScale );
					}

					Vector3 tempVec;
					SAFBIKMatMultVecPreSubAdd( out tempVec, ref headBasis, ref _unityChan_leftEyeDefaultPosition, ref _headBone._defaultPosition, ref headWorldPosition );
					leftEyeWorldPosition += tempVec;
					SAFBIKMatMultVecPreSubAdd( out tempVec, ref headBasis, ref _unityChan_rightEyeDefaultPosition, ref _headBone._defaultPosition, ref headWorldPosition );
					rightEyeWorldPosition += tempVec;

					Matrix3x3 leftEyeBasis, rightEyeBasis;
					SAFBIKMatMult( out leftEyeBasis, ref leftEyeBaseBasis, ref _internalValues.defaultRootBasisInv );
					SAFBIKMatMult( out rightEyeBasis, ref rightEyeBaseBasis, ref _internalValues.defaultRootBasisInv );

					Vector3 worldPosition;
					Quaternion worldRotation;

					if( _leftEyeBone != null && _leftEyeBone.transformIsAlive ) {
						SAFBIKMatMultVecPreSubAdd( out worldPosition, ref leftEyeBasis, ref _leftEyeBone._defaultPosition, ref leftEyeDefaultPosition, ref leftEyeWorldPosition );
						_leftEyeBone.worldPosition = worldPosition;
						SAFBIKMatMultGetRot( out worldRotation, ref leftEyeBaseBasis, ref _leftEyeBone._baseToWorldBasis );
						_leftEyeBone.worldRotation = worldRotation;
					}

					if( _rightEyeBone != null && _rightEyeBone.transformIsAlive ) {
						SAFBIKMatMultVecPreSubAdd( out worldPosition, ref rightEyeBasis, ref _rightEyeBone._defaultPosition, ref rightEyeDefaultPosition, ref rightEyeWorldPosition );
						_rightEyeBone.worldPosition = worldPosition;
						SAFBIKMatMultGetRot( out worldRotation, ref rightEyeBaseBasis, ref _rightEyeBone._baseToWorldBasis );
                        _rightEyeBone.worldRotation = worldRotation;
					}
				}
			}

			void _ResetEyes()
			{
				Quaternion headWorldRotation = _headBone.worldRotation;

				if( _leftEyeBone != null && _leftEyeBone.transformIsAlive ) {
					_leftEyeBone.worldRotation = headWorldRotation * _headToLeftEyeRotation;
				}
				if( _rightEyeBone != null && _rightEyeBone.transformIsAlive ) {
					_rightEyeBone.worldRotation = headWorldRotation * _headToRightEyeRotation;
				}
			}

			void _SolveEyes( ref Matrix3x3 neckBasis, ref Matrix3x3 headBasis, ref Matrix3x3 headBaseBasis,
				ref Quaternion headPrevRotation, ref Quaternion leftEyePrevRotation, ref Quaternion rightEyePrevRotation )
			{
				if( (_leftEyeBone != null && _leftEyeBone.transformIsAlive) || (_rightEyeBone != null && _rightEyeBone.transformIsAlive) ) {
					float _eyesHorzLimitAngle = 40.0f;
					float _eyesVertLimitAngle = 12.0f;
					float _eyesXRate = 0.796f;
					float _eyesYRate = 0.729f;
					float _eyesOuterXRotRate = 0.356f;
					float _eyesInnerXRotRate = 0.212f;

					_internalValues.UpdateDebugValue( "_eyesHorzLimitAngle", ref _eyesHorzLimitAngle );
					_internalValues.UpdateDebugValue( "_eyesVertLimitAngle", ref _eyesVertLimitAngle );
					_internalValues.UpdateDebugValue( "_eyesXRate", ref _eyesXRate );
					_internalValues.UpdateDebugValue( "_eyesYRate", ref _eyesYRate );
					_internalValues.UpdateDebugValue( "_eyesOuterXRotRate", ref _eyesOuterXRotRate );
					_internalValues.UpdateDebugValue( "_eyesInnerXRotRate", ref _eyesInnerXRotRate );

					_eyesHorzLimitAngle *= Mathf.Deg2Rad;
					_eyesVertLimitAngle *= Mathf.Deg2Rad;

					Vector3 headWorldPosition, neckBoneWorldPosition = _neckBone.worldPosition;
                    SAFBIKMatMultVecPreSubAdd( out headWorldPosition, ref neckBasis, ref _headBone._defaultPosition, ref _neckBone._defaultPosition, ref neckBoneWorldPosition );

					Vector3 eyesPosition;
					SAFBIKMatMultVecPreSubAdd( out eyesPosition, ref headBasis, ref _eyesEffector._defaultPosition, ref _headBone._defaultPosition, ref headWorldPosition );

					Vector3 eyesDir = _eyesEffector.worldPosition - eyesPosition;

					SAFBIKMatMultVecInv( out eyesDir, ref headBaseBasis, ref eyesDir );

					SAFBIKVecNormalize( ref eyesDir );

					if( _internalValues.resetTransforms && _eyesEffector.positionWeight < 1.0f - IKEpsilon ) {
						Vector3 tempDir = Vector3.Lerp( new Vector3( 0.0f, 0.0f, 1.0f ), eyesDir, _eyesEffector.positionWeight );
						if( SAFBIKVecNormalize( ref tempDir ) ) {
							eyesDir = tempDir;
						}
					}

					_LimitXY_Square( ref eyesDir,
						Mathf.Sin( _eyesHorzLimitAngle ),
						Mathf.Sin( _eyesHorzLimitAngle ),
						Mathf.Sin( _eyesVertLimitAngle ),
						Mathf.Sin( _eyesVertLimitAngle ) );

					eyesDir.x *= _eyesXRate;
					eyesDir.y *= _eyesYRate;
					Vector3 leftEyeDir = eyesDir;
					Vector3 rightEyeDir = eyesDir;

					if( eyesDir.x >= 0.0f ) {
						leftEyeDir.x *= _eyesInnerXRotRate;
						rightEyeDir.x *= _eyesOuterXRotRate;
					} else {
						leftEyeDir.x *= _eyesOuterXRotRate;
						rightEyeDir.x *= _eyesInnerXRotRate;
					}

					SAFBIKVecNormalize2( ref leftEyeDir, ref rightEyeDir );

					SAFBIKMatMultVec( out leftEyeDir, ref headBaseBasis, ref leftEyeDir );
					SAFBIKMatMultVec( out rightEyeDir, ref headBaseBasis, ref rightEyeDir );

					Quaternion worldRotation;

					if( _leftEyeBone != null && _leftEyeBone.transformIsAlive ) {
						Matrix3x3 leftEyeBaseBasis;
						SAFBIKComputeBasisLockZ( out leftEyeBaseBasis, ref headBasis.column0, ref headBasis.column1, ref leftEyeDir );
						SAFBIKMatMultGetRot( out worldRotation, ref leftEyeBaseBasis, ref _leftEyeBone._baseToWorldBasis );
						if( !_internalValues.resetTransforms && _eyesEffector.positionWeight < 1.0f - IKEpsilon ) {
							Quaternion fromRotation = _headBone.worldRotation * Inverse( headPrevRotation ) * leftEyePrevRotation;
							_leftEyeBone.worldRotation = Quaternion.Lerp( fromRotation, worldRotation, _eyesEffector.positionWeight );
						} else {
							_leftEyeBone.worldRotation = worldRotation;
						}
					}

					if( _rightEyeBone != null && _rightEyeBone.transformIsAlive ) {
						Matrix3x3 rightEyeBaseBasis;
						SAFBIKComputeBasisLockZ( out rightEyeBaseBasis, ref headBasis.column0, ref headBasis.column1, ref rightEyeDir );
						SAFBIKMatMultGetRot( out worldRotation, ref rightEyeBaseBasis, ref _rightEyeBone._baseToWorldBasis );
						if( !_internalValues.resetTransforms && _eyesEffector.positionWeight < 1.0f - IKEpsilon ) {
							Quaternion fromRotation = _headBone.worldRotation * Inverse( headPrevRotation ) * rightEyePrevRotation;
							_rightEyeBone.worldRotation = Quaternion.Lerp( fromRotation, worldRotation, _eyesEffector.positionWeight );
						} else {
							_rightEyeBone.worldRotation = worldRotation;
						}
					}
				}
			}
			
		}
	}
}
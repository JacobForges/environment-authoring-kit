/**
 * @license
 * SPDX-License-Identifier: Apache-2.0
 */

export interface RPGStats {
  level: number;
  xp: number;
  health: number;
  stamina: number;
  currencyGold: number;
  currencyGems: number;
  skills: { name: string; level: number; description: string }[];
  inventory: { id: string; name: string; quantity: number; rarity: string }[];
  equipment: { slot: string; itemId: string; name: string; bonus: string }[];
  biomesDiscovered: string[];
  bossFlags: { [bossId: string]: boolean };
}

export interface PlayerProfile {
  playerId: string; // Firebase UID
  ugsPlayerId: string; // Unity Gaming Services Player ID
  displayName: string;
  level: number;
  xp: number;
  activeAgentId: string;
  cohortIds: string[];
  updatedAt: string;
  rpgStats: RPGStats;
  isLinked: boolean;
  linkCode?: string;
}

export interface AgentProfile {
  agentId: string;
  playerId: string;
  displayName: string;
  xp: number;
  level: number;
  avatarId: string;
  mood: string;
  strikes: number;
  rewardsCount: number;
  trainingMilestones: string[];
  updatedAt: string;
  activeFocus?: string;
  checkpointVersion?: number;
}

export interface Cohort {
  cohortId: string; // e.g. "DTA-SPRING26"
  cohortName: string;
  teacherId: string;
  createdAt: string;
  membersCount: number;
}

export interface LeaderboardEntry {
  id: string; // playerId or agentId
  name: string; // displayName or agentName
  type: 'human' | 'agent';
  level: number;
  xp: number;
  rank?: number;
  detail: string; // "Active Agent" or "Milestone counts"
  cohortId?: string; // Opt association for classroom queries
  email?: string;
}

export interface SyncLogEntry {
  timestamp: string;
  type: 'UPLOAD' | 'CONFLICT_RESOLVED' | 'LINK_GEN' | 'LINK_REDEEM';
  payload: string;
  direction: 'UNITY -> WEB' | 'WEB -> UNITY';
  status: 'SUCCESS' | 'WARNING' | 'ERROR';
}

export interface CheckpointManifest {
  checkpointVersion: number;
  agentId: string;
  sha256: string;
  trainUtc: string;
  focusActivity: string;
  rowsUsed: number;
  rowsRejected: number;
  trainMethod: string;
  onnxFile: string;
  accuracy?: number;
}

export interface EpisodeDataRow {
  episodeId: string;
  activity: string;
  authority: 'DefaultPolicy' | 'ManualOverride' | 'WatchReplay';
  reward: number;
  punishmentSpike: number;
  approved: boolean;
  reason: string;
  timestamp: string;
}

export interface CoachAdvice {
  text: string;
  confidence: 'HIGH' | 'MEDIUM' | 'LOW';
  confidenceFactors: string[];
  decisionTrail: string[];
  provenance: string;
}

export type CosmeticSlot = 'helmet' | 'visor' | 'chassis' | 'trail' | 'emblem' | 'voice';

export type CosmeticRarity = 'Common' | 'Rare' | 'Epic' | 'Legendary';

export interface CosmeticItem {
  id: string;
  name: string;
  description: string;
  slot: CosmeticSlot;
  rarity: CosmeticRarity;
  priceGold: number;
  priceGems: number;
  previewColor?: string;
  iconKey?: string;
  owned: boolean;
  equipped: boolean;
}

export interface AgentAppearance {
  agentId: string;
  equipped: Record<CosmeticSlot, string | null>;
}

export interface CosmeticsInventory {
  agentId: string;
  currencyGold: number;
  currencyGems: number;
  items: CosmeticItem[];
  appearance: AgentAppearance;
}


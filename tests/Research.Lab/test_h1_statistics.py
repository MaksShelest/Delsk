"""Seed-level statistical guardrails. Author: Шелестов Максим Евгеньевич."""
import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from analyze_economy_h1 import paired_stats,summarize

class H1StatisticsTests(unittest.TestCase):
 def test_ten_unanimous_worlds_have_exact_sign_probability(self):
  r=paired_stats([7]*10)
  self.assertEqual(1/512,r['signTestTwoSided']);self.assertEqual([7,7],r['medianBootstrap95'])
 def test_ties_are_not_counted_as_positive_pairs(self):
  r=paired_stats([0]*9+[4]);self.assertEqual(1,r['nonzeroPairs']);self.assertEqual(1,r['signTestTwoSided']);self.assertEqual(0,r['spread']['median'])
 def test_missing_pairs_do_not_become_zero_performance(self):
  r=paired_stats([]);self.assertIsNone(r['spread']);self.assertIsNone(r['medianBootstrap95']);self.assertEqual(1,r['signTestTwoSided'])
 def test_symmetric_pair_result_and_bootstrap_are_reproducible(self):
  x=[-30,-20,-10,-5,-1,1,5,10,20,30];r=paired_stats(x)
  self.assertEqual(1,r['signTestTwoSided']);self.assertEqual(0,r['spread']['median']);self.assertEqual(r,paired_stats(x))
 def test_planned_family_keeps_empty_comparisons_and_failed_attempts(self):
  rows=[dict(map='scenarios/vnext/small-river.json',bots=6,difficulty='Hard',seed=42,status='failed',error='timeout')]
  r=summarize(rows);self.assertEqual(rows,r['failures']);self.assertEqual(4,len(r['pairs']));self.assertTrue(all(p['completeIndependentPairs']==0 for p in r['pairs']))

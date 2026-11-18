import unittest
from render import excel_raster_factor
class NativeDensityTests(unittest.TestCase):
 def test_wide_native_density(self):
  factor=excel_raster_factor(2154,(108,160,1062,168),(88,125,1083,195))
  self.assertGreater(factor,4)
  self.assertGreaterEqual(8*factor,32)
 def test_row_density_floor(self):
  self.assertEqual(8,excel_raster_factor(100,(0,0,100,4),(0,0,120,20)))
 def test_bounds_fail_before_allocation(self):
  for width,diff,crop in [(10000,(0,0,100,10),(0,0,200,20)),(100,(0,0,100,1),(0,0,100,400)),(1000,(0,0,100,10),(0,0,700,200)),(float('inf'),(0,0,100,10),(0,0,200,20)),(100,(0,0,100,0),(0,0,200,20))]:
   with self.assertRaises(ValueError):excel_raster_factor(width,diff,crop)
if __name__=='__main__':unittest.main()

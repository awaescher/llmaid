This folder needs demo images for the brand-detector profile.

Suggested test images (source freely or take photos yourself):

1. branded-street.jpg
   A street photo with visible store signs (Starbucks, H&M, Nike, etc.)

2. office-desk.jpg
   A desk with a laptop (Apple/Dell/Lenovo logo), phone, branded coffee cup

3. no-brands.jpg
   A nature photo or abstract image with zero brand elements (control test)

4. subtle-brands.jpg
   A photo where brands are partially obscured or in the background

Place JPG or PNG files here and run:
  dotnet run --project llmaid -- --profile ./profiles/brand-detector.yaml --targetPath ./testfiles/brand-detector
